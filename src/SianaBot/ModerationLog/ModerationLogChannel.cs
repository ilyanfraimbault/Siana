using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Options;

namespace SianaBot.ModerationLog;

public sealed class ModerationLogOptions
{
    public const string Section = "ModerationLog";

    public ulong? GuildId { get; set; }

    /// <summary>Salon de logs. S'il n'est pas renseigné, le bot cherche <see cref="ChannelName"/> et le crée s'il n'existe pas.</summary>
    public ulong? ChannelId { get; set; }

    public string ChannelName { get; set; } = "logs-moderation";

    /// <summary>
    /// La feature (et les intents qu'elle demande : Guild Moderation pour l'audit log, Server Members et Message Content,
    /// privilégiés) n'est active que si le serveur est configuré.
    /// </summary>
    public bool Enabled => GuildId is not null;
}

/// <summary>Salon de logs réservé aux admins, partagé par les handlers de journalisation, et mise en forme commune des embeds.</summary>
public sealed class ModerationLogChannel(
    DiscordSocketClient client,
    IOptions<ModerationLogOptions> options,
    ILogger<ModerationLogChannel> logger)
{
    private volatile ITextChannel? _channel;

    public ulong? GuildId => options.Value.GuildId;

    public ulong? ChannelId => _channel?.Id;

    public void Attach()
    {
        if (!options.Value.Enabled)
            return;

        client.Ready += () =>
        {
            _ = Task.Run(ResolveChannelAsync);
            return Task.CompletedTask;
        };
    }

    /// <summary>Le serveur journalisé, si <paramref name="guildId"/> est le sien et que le salon de logs est prêt.</summary>
    public SocketGuild? Watched(ulong? guildId) =>
        guildId is not null && guildId == options.Value.GuildId && _channel is not null ? client.GetGuild(guildId.Value) : null;

    public async Task SendAsync(EmbedBuilder embed)
    {
        if (_channel is { } channel)
            await channel.SendMessageAsync(embed: embed.Build(), allowedMentions: AllowedMentions.None);
    }

    private async Task ResolveChannelAsync()
    {
        var settings = options.Value;
        var guild = client.GetGuild(settings.GuildId!.Value);
        if (guild is null)
        {
            logger.LogError("Logs de modération : serveur {GuildId} introuvable", settings.GuildId);
            return;
        }

        try
        {
            _channel = settings.ChannelId is { } channelId
                ? guild.GetTextChannel(channelId)
                : guild.TextChannels.FirstOrDefault(c => c.Name == settings.ChannelName) ?? await CreateChannelAsync(guild, settings.ChannelName);

            if (_channel is null)
                logger.LogError("Logs de modération : salon {ChannelId} introuvable", settings.ChannelId);
        }
        catch (HttpException ex)
        {
            logger.LogError(ex,
                "Logs de modération : impossible de créer #{Channel}. Donne au bot « Gérer les salons », ou crée le salon et renseigne ModerationLog:ChannelId",
                settings.ChannelName);
        }
    }

    /// <summary>Salon masqué à @everyone : seuls les administrateurs (qui voient tout) et le bot y ont accès.</summary>
    private async Task<ITextChannel> CreateChannelAsync(SocketGuild guild, string name)
    {
        var channel = await guild.CreateTextChannelAsync(name, props =>
        {
            props.Topic = "Actions de modération et activité du serveur, journalisées par le bot.";
            props.PermissionOverwrites = new List<Overwrite>
            {
                new(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
                new(client.CurrentUser.Id, PermissionTarget.User, new OverwritePermissions(
                    viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, embedLinks: PermValue.Allow)),
            };
        });
        logger.LogInformation("Logs de modération : salon #{Channel} créé ({ChannelId})", channel.Name, channel.Id);
        return channel;
    }

    public static EmbedBuilder Titled(string title, Color color) => new EmbedBuilder().WithTitle(title).WithColor(color);

    /// <summary>Description et miniature communes : le membre concerné, son pseudo sur le serveur et son id.</summary>
    public static EmbedBuilder About(EmbedBuilder embed, IUser? user, SocketGuild guild, ulong userId)
    {
        var member = guild.GetUser(userId);
        return embed.WithDescription($"<@{userId}> · {DisplayName(member, user)} (`{userId}`)")
            .WithThumbnailUrl(member?.GetDisplayAvatarUrl() ?? user?.GetDisplayAvatarUrl());
    }

    /// <summary>Footer commun à tous les logs : l'auteur de l'action, avec son pseudo sur le serveur et son avatar.</summary>
    public static EmbedBuilder By(EmbedBuilder embed, IUser user, SocketGuild guild)
    {
        var member = guild.GetUser(user.Id);
        return embed.WithFooter(DisplayName(member, user), member?.GetDisplayAvatarUrl() ?? user.GetDisplayAvatarUrl());
    }

    public static string DisplayName(IGuildUser? member, IUser? user) =>
        member?.DisplayName ?? user?.GlobalName ?? user?.Username ?? "Utilisateur inconnu";

    /// <summary>Texte du message, suivi du nom de ses pièces jointes (leur lien expire avec le message).</summary>
    public static string Content(IMessage message)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(message.Content))
            parts.Add(message.Content);
        parts.AddRange(message.Attachments.Select(a => $"📎 `{a.Filename}`"));
        if (message.Stickers.Count > 0)
            parts.Add($"Sticker : {string.Join(", ", message.Stickers.Select(s => s.Name))}");
        return parts.Count > 0 ? Truncate(string.Join('\n', parts)) : "*vide*";
    }

    /// <summary>Tronque <paramref name="text"/> à <paramref name="max"/> caractères (1024 : limite d'un champ d'embed).</summary>
    public static string Truncate(string text, int max = EmbedFieldBuilder.MaxFieldValueLength) =>
        text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");
}
