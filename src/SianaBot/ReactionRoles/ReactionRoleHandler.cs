using Discord;
using Discord.WebSocket;

namespace SianaBot.ReactionRoles;

public sealed class ReactionRoleHandler(
    DiscordSocketClient client,
    ReactionRoleStore store,
    ILogger<ReactionRoleHandler> logger)
{
    public void Attach()
    {
        client.ReactionAdded += (message, _, reaction) => HandleAsync(message.Id, reaction, add: true);
        client.ReactionRemoved += (message, _, reaction) => HandleAsync(message.Id, reaction, add: false);
    }

    private async Task HandleAsync(ulong messageId, SocketReaction reaction, bool add)
    {
        if (reaction.UserId == client.CurrentUser.Id)
            return;
        if (reaction.User.IsSpecified && reaction.User.Value.IsBot)
            return;

        var reactionRole = store.Find(messageId, Emotes.Key(reaction.Emote));
        if (reactionRole is null)
            return;

        try
        {
            // Passage par le REST : pas besoin du cache des membres (ni de l'intent privilégié GuildMembers).
            if (add)
                await client.Rest.AddRoleAsync(reactionRole.GuildId, reaction.UserId, reactionRole.RoleId);
            else
                await client.Rest.RemoveRoleAsync(reactionRole.GuildId, reaction.UserId, reactionRole.RoleId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Impossible de {Action} le rôle {RoleId} à {UserId} sur {GuildId}",
                add ? "donner" : "retirer", reactionRole.RoleId, reaction.UserId, reactionRole.GuildId);
        }
    }
}
