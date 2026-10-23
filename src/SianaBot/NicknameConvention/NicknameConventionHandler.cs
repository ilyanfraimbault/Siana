using Discord.WebSocket;
using Microsoft.Extensions.Options;

namespace SianaBot.NicknameConvention;

public sealed class NicknameConventionHandler(
    DiscordSocketClient client,
    NicknameConventionService service,
    IOptions<NicknameConventionOptions> options,
    ILogger<NicknameConventionHandler> logger)
{
    public void Attach()
    {
        if (!options.Value.Enabled)
            return;

        client.UserJoined += member => Run(member);
        client.GuildMemberUpdated += (before, member) => Run(member);
        // Rattrape les changements de pseudo faits pendant que le bot était hors ligne.
        client.Ready += () =>
        {
            _ = Task.Run(SyncAsync);
            return Task.CompletedTask;
        };
    }

    private Task Run(SocketGuildUser member)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await service.ApplyAsync(member);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Convention de pseudo non appliquée à {UserId}", member.Id);
            }
        });
        return Task.CompletedTask;
    }

    private async Task SyncAsync()
    {
        try
        {
            var (added, removed) = await service.SyncAsync();
            if (added + removed > 0)
                logger.LogInformation("Convention de pseudo : {Added} rôle(s) ajouté(s), {Removed} retiré(s)", added, removed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Synchronisation de la convention de pseudo échouée");
        }
    }
}
