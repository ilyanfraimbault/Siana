using Discord;
using Discord.Rest;

namespace SianaBot.ReactionRoles;

public sealed record PanelEntry(IEmote Emote, ulong RoleId, string Label);

/// <summary>Logique des reaction roles, partagée entre le bot (events de la gateway) et la CLI (REST seul).</summary>
public sealed class ReactionRoleService(
    DiscordRestClient rest,
    ReactionRoleStore store,
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
        if (member is null)
            return true;

        if (member.RoleIds.Contains(reactionRole.RoleId))
            await rest.RemoveRoleAsync(reactionRole.GuildId, userId, reactionRole.RoleId);
        else
            await rest.AddRoleAsync(reactionRole.GuildId, userId, reactionRole.RoleId);
        return true;
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

    /// <summary>Publie un embed de choix de rôles en mode toggle et enregistre ses associations.</summary>
    public async Task<IUserMessage> CreatePanelAsync(
        ulong channelId, string title, string? description, Color color, IReadOnlyList<PanelEntry> entries)
    {
        var channel = await rest.GetChannelAsync(channelId) as ITextChannel
            ?? throw new InvalidOperationException($"Salon textuel {channelId} introuvable.");

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(color)
            .WithFooter("Ta réaction disparaît aussitôt : c'est normal, ton choix est pris en compte.");
        foreach (var entry in entries)
            embed.AddField($"{entry.Emote}  {entry.Label}", $"<@&{entry.RoleId}>", inline: true);

        var message = await channel.SendMessageAsync(embed: embed.Build(), allowedMentions: AllowedMentions.None);
        foreach (var entry in entries)
            await message.AddReactionAsync(entry.Emote);

        await store.UpsertManyAsync(entries
            .Select(e => new ReactionRole(
                channel.GuildId, channel.Id, message.Id, Emotes.Key(e.Emote), e.Emote.ToString()!, e.RoleId, ReactionRoleMode.Toggle))
            .ToList());
        return message;
    }

    private async Task<IUserMessage?> GetMessageAsync(ulong channelId, ulong messageId) =>
        await rest.GetChannelAsync(channelId) is IMessageChannel channel
            ? await channel.GetMessageAsync(messageId) as IUserMessage
            : null;

    private static async Task<IEnumerable<IUser>> GetReactorsAsync(IUserMessage message, IEmote emote) =>
        (await message.GetReactionUsersAsync(emote, MaxReactionUsers).FlattenAsync()).Where(u => !u.IsBot);
}
