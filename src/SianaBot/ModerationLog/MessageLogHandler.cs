using Discord;
using Discord.WebSocket;
using static SianaBot.ModerationLog.ModerationLogChannel;

namespace SianaBot.ModerationLog;

/// <summary>
/// Journalise les messages modifiés et supprimés. Le contenu d'origine vient du cache de messages du bot :
/// il n'est connu que pour les messages reçus depuis son démarrage. Les messages des bots ne sont pas journalisés.
/// </summary>
public sealed class MessageLogHandler(
    DiscordSocketClient client,
    ModerationLogChannel channel,
    ILogger<MessageLogHandler> logger)
{
    private const string Unavailable = "*inconnu : message antérieur au démarrage du bot*";

    public void Attach()
    {
        if (channel.GuildId is null)
            return;

        client.MessageUpdated += (before, after, source) => Run(() => OnUpdatedAsync(before, after, source));
        client.MessageDeleted += (message, source) => Run(() => OnDeletedAsync(message, source.Id));
        client.MessagesBulkDeleted += (messages, source) => Run(() => OnBulkDeletedAsync(messages, source.Id));
    }

    private Task Run(Func<Task> handler)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Log de message non envoyé");
            }
        });
        return Task.CompletedTask;
    }

    private async Task OnUpdatedAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel source)
    {
        // Sans date de modification, c'est Discord qui complète le message (aperçu de lien…), pas l'auteur.
        if (after.Author.IsBot || after.EditedTimestamp is null || Watched(source.Id) is not { } guild)
            return;
        if (before.HasValue && before.Value.Content == after.Content)
            return;

        var embed = About(Titled("✏️ Message modifié", Color.Gold), after.Author, guild, after.Author.Id)
            .AddField("Salon", $"<#{source.Id}> · [aller au message]({after.GetJumpUrl()})")
            .AddField("Avant", before.HasValue ? Content(before.Value) : Unavailable)
            .AddField("Après", Content(after))
            .WithTimestamp(after.EditedTimestamp.Value);
        await channel.SendAsync(embed);
    }

    private async Task OnDeletedAsync(Cacheable<IMessage, ulong> message, ulong channelId)
    {
        if (Watched(channelId) is not { } guild || message.Value?.Author.IsBot == true)
            return;

        var embed = Titled("🗑️ Message supprimé", Color.DarkRed).WithCurrentTimestamp();
        if (message.Value is { } deleted)
        {
            About(embed, deleted.Author, guild, deleted.Author.Id)
                .AddField("Salon", $"<#{channelId}> · envoyé <t:{deleted.Timestamp.ToUnixTimeSeconds()}:R>")
                .AddField("Contenu", Content(deleted));
        }
        else
        {
            embed.WithDescription(Unavailable).AddField("Salon", $"<#{channelId}> · message `{message.Id}`");
        }
        await channel.SendAsync(embed);
    }

    private async Task OnBulkDeletedAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> messages, ulong channelId)
    {
        if (Watched(channelId) is null)
            return;

        var known = messages.Where(m => m.HasValue && !m.Value.Author.IsBot).Select(m => m.Value).OrderBy(m => m.Timestamp)
            .Select(m => $"**{m.Author.Username}** : {Content(m)}");
        var embed = Titled($"🧹 {messages.Count} messages supprimés en masse", Color.DarkRed)
            .WithDescription(Truncate(string.Join('\n', known) is { Length: > 0 } list ? list : Unavailable, EmbedBuilder.MaxDescriptionLength))
            .AddField("Salon", $"<#{channelId}>")
            .WithCurrentTimestamp();
        await channel.SendAsync(embed);
    }

    /// <summary>Le serveur journalisé, si le salon en fait partie. Le salon de logs lui-même est ignoré.</summary>
    private SocketGuild? Watched(ulong channelId) =>
        channelId != channel.ChannelId && client.GetChannel(channelId) is SocketGuildChannel source ? channel.Watched(source.Guild.Id) : null;

    /// <summary>Texte du message, suivi du nom de ses pièces jointes (leur lien expire avec le message).</summary>
    private static string Content(IMessage message)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(message.Content))
            parts.Add(message.Content);
        parts.AddRange(message.Attachments.Select(a => $"📎 `{a.Filename}`"));
        if (message.Stickers.Count > 0)
            parts.Add($"Sticker : {string.Join(", ", message.Stickers.Select(s => s.Name))}");
        return parts.Count > 0 ? Truncate(string.Join('\n', parts)) : "*vide*";
    }
}
