using Discord;
using Discord.WebSocket;

namespace SianaBot.ReactionRoles;

public sealed class ReactionRoleHandler(
    DiscordSocketClient client,
    ReactionRoleStore store,
    ReactionRoleService service,
    ILogger<ReactionRoleHandler> logger)
{
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public void Attach()
    {
        // Hors de la gateway : un rate limit REST ne doit pas bloquer la réception des events.
        client.ReactionAdded += (message, channel, reaction) =>
        {
            _ = Task.Run(() => OnReactionAddedAsync(message, reaction));
            return Task.CompletedTask;
        };
        client.ReactionRemoved += (message, channel, reaction) =>
        {
            _ = Task.Run(() => OnReactionRemovedAsync(message.Id, reaction));
            return Task.CompletedTask;
        };
        // À chaque (re)connexion, on rattrape les réactions posées pendant la coupure.
        client.Ready += () =>
        {
            _ = Task.Run(SyncAsync);
            return Task.CompletedTask;
        };
    }

    private async Task OnReactionAddedAsync(Cacheable<IUserMessage, ulong> message, SocketReaction reaction)
    {
        var reactionRole = Find(message.Id, reaction);
        if (reactionRole is null)
            return;

        try
        {
            if (await service.OnReactionAddedAsync(reactionRole, reaction.UserId))
                await (await message.GetOrDownloadAsync()).RemoveReactionAsync(reaction.Emote, reaction.UserId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reaction role {RoleId} non appliqué à {UserId}", reactionRole.RoleId, reaction.UserId);
        }
    }

    private async Task OnReactionRemovedAsync(ulong messageId, SocketReaction reaction)
    {
        var reactionRole = Find(messageId, reaction);
        if (reactionRole is null)
            return;

        try
        {
            await service.OnReactionRemovedAsync(reactionRole, reaction.UserId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reaction role {RoleId} non retiré à {UserId}", reactionRole.RoleId, reaction.UserId);
        }
    }

    private ReactionRole? Find(ulong messageId, SocketReaction reaction)
    {
        if (reaction.UserId == client.CurrentUser.Id)
            return null;
        if (reaction.User.IsSpecified && reaction.User.Value.IsBot)
            return null;

        return store.Find(messageId, Emotes.Key(reaction.Emote));
    }

    private async Task SyncAsync()
    {
        if (!await _syncLock.WaitAsync(0))
            return;
        try
        {
            var processed = await service.SyncAsync();
            if (processed > 0)
                logger.LogInformation("Rattrapage des reaction roles : {Count} réaction(s) traitée(s)", processed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Rattrapage des reaction roles échoué");
        }
        finally
        {
            _syncLock.Release();
        }
    }
}
