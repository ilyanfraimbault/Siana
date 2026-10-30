using Discord;
using Discord.WebSocket;
using static SianaBot.ModerationLog.ModerationLogChannel;

namespace SianaBot.ModerationLog;

/// <summary>
/// Journalise les messages privés reçus par le bot, pour voir les réponses aux <c>/mp</c>.
/// </summary>
public sealed class DirectMessageLogHandler(
    DiscordSocketClient client,
    ModerationLogChannel channel,
    ILogger<DirectMessageLogHandler> logger)
{
    public void Attach()
    {
        if (channel.GuildId is null)
            return;

        client.MessageReceived += message =>
        {
            _ = Task.Run(() => OnReceivedAsync(message));
            return Task.CompletedTask;
        };
    }

    private async Task OnReceivedAsync(SocketMessage message)
    {
        if (message.Channel is not IDMChannel || message.Author.IsBot || channel.Watched(channel.GuildId) is not { } guild)
            return;

        try
        {
            var embed = About(Titled("📩 Message privé reçu", Color.Teal), message.Author, guild, message.Author.Id)
                .AddField("Contenu", Content(message))
                .WithTimestamp(message.Timestamp);
            By(embed, message.Author, guild);
            // Le bot est le seul à voir la conversation : les liens permettent d'ouvrir les pièces jointes.
            if (message.Attachments.Count > 0)
                embed.AddField("Pièces jointes", Truncate(string.Join('\n', message.Attachments.Select(a => $"[{a.Filename}]({a.Url})"))));

            await channel.SendAsync(embed);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Log de MP non envoyé (de {UserId})", message.Author.Id);
        }
    }
}
