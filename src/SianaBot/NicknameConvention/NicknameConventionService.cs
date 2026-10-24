using System.Text.RegularExpressions;
using Discord;
using Discord.Rest;
using Microsoft.Extensions.Options;

namespace SianaBot.NicknameConvention;

public sealed class NicknameConventionOptions
{
    public const string Section = "NicknameConvention";

    public ulong? GuildId { get; set; }

    /// <summary>Rôle donné aux membres dont le pseudo respecte la convention, retiré sinon.</summary>
    public ulong? RoleId { get; set; }

    /// <summary>
    /// Rôles réservés aux membres qui ont <see cref="RoleId"/> (les filières). Choisis sans pseudo conforme, ils restent
    /// en attente ; perdre <see cref="RoleId"/> les remet en attente jusqu'à ce que le pseudo soit corrigé.
    /// </summary>
    public List<ulong> ReservedRoleIds { get; set; } = [];

    public string PendingStoragePath { get; set; } = "data/pending-roles.json";

    /// <summary>La feature (et l'intent privilégié GuildMembers) n'est active que si le serveur et le rôle sont configurés.</summary>
    public bool Enabled => GuildId is not null && RoleId is not null;
}

/// <summary>Ce qu'un passage sur un membre a changé.</summary>
public readonly record struct AccessChange(bool? ConventionRole, int Restored, int Suspended)
{
    public bool Any => ConventionRole is not null || Restored > 0 || Suspended > 0;
}

/// <summary>Convention de pseudo de la promo (« Prénom N. ») et rôles réservés aux pseudos conformes.</summary>
public sealed partial class NicknameConventionService(
    DiscordRestClient rest,
    PendingRoleStore pending,
    IOptions<NicknameConventionOptions> options,
    ILogger<NicknameConventionService> logger)
{
    /// <summary>
    /// Un prénom en lettres (accents et prénoms composés « Jean-Marc » acceptés), une espace,
    /// 1 à 4 lettres du nom (pour les noms composés : « Ilyan FR. »), un point facultatif. Pas de chiffre, pas d'autre caractère.
    /// </summary>
    [GeneratedRegex(@"^\p{L}+(?:-\p{L}+)* \p{L}{1,4}\.?$")]
    private static partial Regex ConventionRegex();

    public static bool Matches(string? name) => name is not null && ConventionRegex().IsMatch(name);

    /// <summary>Met le rôle de la convention et les rôles réservés en accord avec le pseudo.</summary>
    public async Task<AccessChange> ApplyAsync(IGuildUser member)
    {
        var settings = options.Value;
        if (!settings.Enabled || member.IsBot || member.GuildId != settings.GuildId)
            return default;

        var conventionRoleId = settings.RoleId!.Value;
        var roles = member.RoleIds.ToHashSet();
        var compliant = Matches(member.DisplayName);

        bool? conventionRole = null;
        if (compliant && roles.Add(conventionRoleId))
        {
            await member.AddRoleAsync(conventionRoleId);
            conventionRole = true;
        }
        else if (!compliant && roles.Remove(conventionRoleId))
        {
            await member.RemoveRoleAsync(conventionRoleId);
            conventionRole = false;
        }

        if (compliant)
        {
            // Pseudo conforme : on active les choix en attente.
            var waiting = pending.For(member.GuildId, member.Id);
            var restore = waiting.Where(r => settings.ReservedRoleIds.Contains(r) && !roles.Contains(r)).ToList();
            if (restore.Count > 0)
                await member.AddRolesAsync(restore);
            if (waiting.Count > 0)
                await pending.RemoveAsync(member.GuildId, member.Id, waiting.ToList());
            return new AccessChange(conventionRole, restore.Count, 0);
        }

        // Pseudo non conforme : les rôles réservés passent en attente.
        var suspend = settings.ReservedRoleIds.Where(roles.Contains).ToList();
        if (suspend.Count > 0)
        {
            await pending.AddAsync(member.GuildId, member.Id, suspend);
            await member.RemoveRolesAsync(suspend);
        }
        return new AccessChange(conventionRole, 0, suspend.Count);
    }

    /// <summary>
    /// Clic sur un rôle réservé par quelqu'un sans pseudo conforme : le choix est mis (ou retiré) de l'attente.
    /// Renvoie false si le clic doit être traité normalement.
    /// </summary>
    public async Task<bool> TryToggleReservedAsync(IGuildUser member, ulong roleId)
    {
        var settings = options.Value;
        if (!settings.Enabled
            || member.GuildId != settings.GuildId
            || !settings.ReservedRoleIds.Contains(roleId)
            || member.RoleIds.Contains(settings.RoleId!.Value))
            return false;

        if (pending.For(member.GuildId, member.Id).Contains(roleId))
            await pending.RemoveAsync(member.GuildId, member.Id, [roleId]);
        else
            await pending.AddAsync(member.GuildId, member.Id, [roleId]);
        return true;
    }

    /// <summary>Passe sur tous les membres du serveur.</summary>
    public async Task<(int Added, int Removed, int Restored, int Suspended)> SyncAsync()
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return default;

        var guild = await rest.GetGuildAsync(settings.GuildId!.Value);
        var members = await guild.GetUsersAsync().FlattenAsync();

        int added = 0, removed = 0, restored = 0, suspended = 0;
        foreach (var member in members)
        {
            try
            {
                var change = await ApplyAsync(member);
                if (change.ConventionRole == true) added++;
                if (change.ConventionRole == false) removed++;
                restored += change.Restored;
                suspended += change.Suspended;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Convention de pseudo non appliquée à {UserId}", member.Id);
            }
        }
        return (added, removed, restored, suspended);
    }
}
