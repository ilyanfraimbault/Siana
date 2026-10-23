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

    /// <summary>La feature (et l'intent privilégié GuildMembers) n'est active que si le serveur et le rôle sont configurés.</summary>
    public bool Enabled => GuildId is not null && RoleId is not null;
}

/// <summary>Convention de pseudo de la promo : « Prénom N » ou « Prénom N. ».</summary>
public sealed partial class NicknameConventionService(
    DiscordRestClient rest,
    IOptions<NicknameConventionOptions> options,
    ILogger<NicknameConventionService> logger)
{
    /// <summary>
    /// Un prénom en lettres (accents et prénoms composés « Jean-Marc » acceptés), une espace,
    /// l'initiale du nom, un point facultatif. Pas de chiffre, pas d'autre caractère.
    /// </summary>
    [GeneratedRegex(@"^\p{L}+(?:-\p{L}+)* \p{L}\.?$")]
    private static partial Regex ConventionRegex();

    public static bool Matches(string? name) => name is not null && ConventionRegex().IsMatch(name);

    /// <summary>Met le rôle en accord avec le pseudo. Renvoie true si ajouté, false si retiré, null si rien à faire.</summary>
    public async Task<bool?> ApplyAsync(IGuildUser member)
    {
        var settings = options.Value;
        if (!settings.Enabled || member.IsBot || member.GuildId != settings.GuildId)
            return null;

        var roleId = settings.RoleId!.Value;
        var hasRole = member.RoleIds.Contains(roleId);
        var matches = Matches(member.DisplayName);

        if (matches && !hasRole)
        {
            await member.AddRoleAsync(roleId);
            return true;
        }
        if (!matches && hasRole)
        {
            await member.RemoveRoleAsync(roleId);
            return false;
        }
        return null;
    }

    /// <summary>Passe sur tous les membres du serveur.</summary>
    public async Task<(int Added, int Removed)> SyncAsync()
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return (0, 0);

        var guild = await rest.GetGuildAsync(settings.GuildId!.Value);
        var members = await guild.GetUsersAsync().FlattenAsync();

        int added = 0, removed = 0;
        foreach (var member in members)
        {
            try
            {
                switch (await ApplyAsync(member))
                {
                    case true: added++; break;
                    case false: removed++; break;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Convention de pseudo non appliquée à {UserId}", member.Id);
            }
        }
        return (added, removed);
    }
}
