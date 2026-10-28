using Discord;
using Discord.Rest;
using SianaBot.NicknameConvention;

namespace SianaBot.ReactionRoles;

public enum ToggleOutcome { Added, Removed, Pending, PendingCancelled }

/// <param name="Replaced">Rôles exclusifs retirés pour faire place au nouveau.</param>
public sealed record ToggleResult(ToggleOutcome Outcome, IReadOnlyList<ulong> Replaced);

/// <summary>Logique des reaction roles, partagée entre le bot (events de la gateway) et la CLI (REST seul).</summary>
public sealed class ReactionRoleService(
    DiscordRestClient rest,
    ReactionRoleStore store,
    NicknameConventionService nicknameConvention,
    ILogger<ReactionRoleService> logger)
{
    private const int MaxReactionUsers = 1000;

    /// <summary>Applique un clic sur une réaction. Renvoie true si la réaction de l'utilisateur doit être retirée.</summary>
    public async Task<bool> OnReactionAddedAsync(ReactionRole reactionRole, ulong userId)
    {
        if (reactionRole.Mode != ReactionRoleMode.Toggle)
        {
            await rest.AddRoleAsync(reactionRole.GuildId, userId, reactionRole.RoleId);
            return false;
        }

        var member = await rest.GetGuildUserAsync(reactionRole.GuildId, userId);
        if (member is not null)
            await ToggleAsync(member, reactionRole.RoleId, []);
        return true;
    }

    /// <summary>Inverse un rôle. Prendre le rôle retire ceux de <paramref name="exclusiveWith"/>.</summary>
    public async Task<ToggleResult> ToggleAsync(IGuildUser member, ulong roleId, IReadOnlyCollection<ulong> exclusiveWith)
    {
        var others = exclusiveWith.Where(r => r != roleId).ToList();

        // Rôle réservé aux pseudos conformes : le choix passe en attente.
        if (await nicknameConvention.TryToggleReservedAsync(member, roleId, others) is { } pending)
            return new ToggleResult(pending ? ToggleOutcome.Pending : ToggleOutcome.PendingCancelled, []);

        if (member.RoleIds.Contains(roleId))
        {
            await member.RemoveRoleAsync(roleId);
            return new ToggleResult(ToggleOutcome.Removed, []);
        }

        var replaced = others.Where(member.RoleIds.Contains).ToList();
        await member.AddRoleAsync(roleId);
        if (replaced.Count > 0)
            await member.RemoveRolesAsync(replaced);
        return new ToggleResult(ToggleOutcome.Added, replaced);
    }

    public async Task OnReactionRemovedAsync(ReactionRole reactionRole, ulong userId)
    {
        // En mode toggle, c'est le bot qui retire les réactions : ça ne doit pas toucher au rôle.
        if (reactionRole.Mode == ReactionRoleMode.Toggle)
            return;

        await rest.RemoveRoleAsync(reactionRole.GuildId, userId, reactionRole.RoleId);
    }

    /// <summary>
    /// Rattrape les réactions posées pendant que le bot était hors ligne, sur tous les messages suivis.
    /// Renvoie le nombre de réactions traitées.
    /// </summary>
    public async Task<int> SyncAsync()
    {
        var processed = 0;
        foreach (var group in store.All().GroupBy(r => (r.ChannelId, r.MessageId)))
        {
            var message = await GetMessageAsync(group.Key.ChannelId, group.Key.MessageId);
            if (message is null)
            {
                logger.LogWarning("Message {MessageId} introuvable, reaction roles ignorés", group.Key.MessageId);
                continue;
            }

            foreach (var reactionRole in group)
            {
                var emote = Emotes.Parse(reactionRole.Emote);
                foreach (var user in await GetReactorsAsync(message, emote))
                {
                    try
                    {
                        if (await OnReactionAddedAsync(reactionRole, user.Id))
                            await message.RemoveReactionAsync(emote, user.Id);
                        processed++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Rattrapage impossible pour {UserId} sur {MessageId}", user.Id, message.Id);
                    }
                }
            }
        }
        return processed;
    }

    /// <summary>Donne le rôle à tous ceux qui ont réagi avec cet emoji, sur un message non suivi. Renvoie le nombre de rôles ajoutés.</summary>
    public async Task<int> GrantFromReactionsAsync(ulong channelId, ulong messageId, IEmote emote, ulong roleId)
    {
        var channel = await rest.GetChannelAsync(channelId) as IGuildChannel
            ?? throw new InvalidOperationException($"Salon {channelId} introuvable.");
        var message = await GetMessageAsync(channelId, messageId)
            ?? throw new InvalidOperationException($"Message {messageId} introuvable.");

        var granted = 0;
        foreach (var user in await GetReactorsAsync(message, emote))
        {
            var member = await rest.GetGuildUserAsync(channel.GuildId, user.Id);
            if (member is null || member.RoleIds.Contains(roleId))
                continue;

            await member.AddRoleAsync(roleId);
            granted++;
        }
        return granted;
    }

    /// <summary>Publie un panneau de choix de rôles à boutons : un clic donne le rôle, un second le retire.</summary>
    public async Task<IUserMessage> CreatePanelAsync(
        ulong channelId, string title, string? description, Color color, IReadOnlyList<PanelEntry> entries, bool exclusive)
    {
        var channel = await rest.GetChannelAsync(channelId) as ITextChannel
            ?? throw new InvalidOperationException($"Salon textuel {channelId} introuvable.");

        var (embed, components) = RolePanel.Build(title, description, color, entries, exclusive);
        return await channel.SendMessageAsync(embed: embed, components: components, allowedMentions: AllowedMentions.None);
    }

    /// <summary>
    /// Remplace les réactions d'un ancien panneau (publié par le bot) par des boutons, sur le même message,
    /// et cesse de suivre ses réactions.
    /// </summary>
    public async Task<IUserMessage> ConvertPanelAsync(ulong channelId, ulong messageId, bool exclusive)
    {
        var message = await GetMessageAsync(channelId, messageId)
            ?? throw new InvalidOperationException($"Message {messageId} introuvable.");
        if (message.Author.Id != rest.CurrentUser.Id)
            throw new InvalidOperationException("Ce message n'a pas été publié par le bot : impossible d'y mettre des boutons.");

        var tracked = store.All().Where(r => r.MessageId == messageId).ToList();
        if (tracked.Count == 0)
            throw new InvalidOperationException("Aucun reaction role suivi sur ce message.");

        var embed = message.Embeds.FirstOrDefault();
        var guild = await rest.GetGuildAsync(tracked[0].GuildId);
        var entries = tracked
            .Select(r => new PanelEntry(Emotes.Parse(r.Emote), r.RoleId, LabelFor(embed, r) ?? guild.GetRole(r.RoleId)?.Name ?? r.RoleId.ToString()))
            .ToList();

        var (panel, components) = RolePanel.Build(
            embed?.Title, embed?.Description, embed?.Color ?? RolePanel.DefaultColor, entries, exclusive);
        await message.ModifyAsync(m =>
        {
            m.Embed = panel;
            m.Components = components;
        });
        await message.RemoveAllReactionsAsync();
        await store.RemoveMessageAsync(messageId);
        return message;
    }

    /// <summary>Libellé d'un ancien panneau : champ « emoji  libellé » dont la valeur mentionne le rôle.</summary>
    private static string? LabelFor(IEmbed? embed, ReactionRole reactionRole) =>
        embed?.Fields.FirstOrDefault(f => f.Value == $"<@&{reactionRole.RoleId}>") is { } field
            && field.Name.Replace(reactionRole.Emote, "").Trim() is { Length: > 0 } label
            ? label
            : null;

    private async Task<IUserMessage?> GetMessageAsync(ulong channelId, ulong messageId) =>
        await rest.GetChannelAsync(channelId) is IMessageChannel channel
            ? await channel.GetMessageAsync(messageId) as IUserMessage
            : null;

    private static async Task<IEnumerable<IUser>> GetReactorsAsync(IUserMessage message, IEmote emote) =>
        (await message.GetReactionUsersAsync(emote, MaxReactionUsers).FlattenAsync()).Where(u => !u.IsBot);
}
