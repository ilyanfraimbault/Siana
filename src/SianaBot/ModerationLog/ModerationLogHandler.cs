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

    /// <summary>La feature (et l'intent Guild Moderation, qui porte les entrées d'audit log) n'est active que si le serveur est configuré.</summary>
    public bool Enabled => GuildId is not null;
}

/// <summary>
/// Journalise dans un salon réservé aux admins les actions de modération (ban, kick, timeout, mute vocal) et les changements
/// de rôles, d'après l'audit log du serveur : elles sont prises en compte qu'elles viennent de Discord, d'un bot ou de Siana.
/// </summary>
public sealed class ModerationLogHandler(
    DiscordSocketClient client,
    IOptions<ModerationLogOptions> options,
    ILogger<ModerationLogHandler> logger)
{
    private volatile ITextChannel? _channel;

    public void Attach()
    {
        if (!options.Value.Enabled)
            return;

        client.Ready += () =>
        {
            _ = Task.Run(ResolveChannelAsync);
            return Task.CompletedTask;
        };
        // Hors de la gateway : l'envoi (et l'éventuel appel REST pour l'auteur) ne doit pas bloquer la réception des events.
        client.AuditLogCreated += (entry, guild) =>
        {
            _ = Task.Run(() => OnAuditLogCreatedAsync(entry, guild));
            return Task.CompletedTask;
        };
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
            props.Topic = "Actions de modération et changements de rôles, journalisés par le bot.";
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

    private async Task OnAuditLogCreatedAsync(SocketAuditLogEntry entry, SocketGuild guild)
    {
        if (guild.Id != options.Value.GuildId || _channel is not { } channel)
            return;

        var (target, embed) = entry.Data switch
        {
            SocketBanAuditLogData d => (d.Target, Titled("🔨 Membre banni", Color.Red)),
            SocketUnbanAuditLogData d => (d.Target, Titled("♻️ Membre débanni", Color.Green)),
            SocketKickAuditLogData d => (d.Target, Titled("👢 Membre expulsé", Color.Orange)),
            SocketMemberUpdateAuditLogData d => (d.Target, DescribeMemberUpdate(d)),
            SocketMemberRoleAuditLogData d => (d.Target, DescribeRoles(d)),
            _ => default,
        };
        if (embed is null)
            return;

        try
        {
            var user = await target.GetOrDownloadAsync();
            var member = guild.GetUser(target.Id);
            embed.WithDescription($"<@{target.Id}> · {DisplayName(member, user)} (`{target.Id}`)")
                .WithThumbnailUrl(member?.GetDisplayAvatarUrl() ?? user?.GetDisplayAvatarUrl())
                .WithTimestamp(entry.CreatedAt);

            if (!string.IsNullOrWhiteSpace(entry.Reason))
                embed.AddField("Raison", entry.Reason);

            if (await GetExecutorAsync(entry, guild) is { } executor)
            {
                var executorMember = guild.GetUser(executor.Id);
                embed.WithFooter(DisplayName(executorMember, executor),
                    executorMember?.GetDisplayAvatarUrl() ?? executor.GetDisplayAvatarUrl());
            }

            await channel.SendMessageAsync(embed: embed.Build(), allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Log de modération non envoyé ({Action} sur {UserId})", entry.Action, target.Id);
        }
    }

    /// <summary>Timeout et mute / sourdine vocale. Les autres changements (pseudo…) ne sont pas journalisés.</summary>
    private static EmbedBuilder? DescribeMemberUpdate(SocketMemberUpdateAuditLogData data)
    {
        var (before, after) = (data.Before, data.After);

        if (after.TimedOutUntil is { } until && until > DateTimeOffset.UtcNow && until != before.TimedOutUntil)
            return Titled("⏳ Membre exclu temporairement", Color.Orange)
                .AddField("Jusqu'au", $"<t:{until.ToUnixTimeSeconds()}:f> (<t:{until.ToUnixTimeSeconds()}:R>)");
        if (before.TimedOutUntil is not null && after.TimedOutUntil is null)
            return Titled("⌛ Exclusion temporaire levée", Color.Green);

        if (after.Mute is { } muted && muted != before.Mute)
            return muted ? Titled("🔇 Membre rendu muet (vocal)", Color.Orange) : Titled("🔊 Membre n'est plus muet (vocal)", Color.Green);
        if (after.Deaf is { } deafened && deafened != before.Deaf)
            return deafened ? Titled("🔇 Membre mis en sourdine (vocal)", Color.Orange) : Titled("🔊 Membre n'est plus en sourdine (vocal)", Color.Green);

        return null;
    }

    private static EmbedBuilder? DescribeRoles(SocketMemberRoleAuditLogData data)
    {
        var added = data.Roles.Where(r => r.Added).Select(r => $"<@&{r.RoleId}>").ToList();
        var removed = data.Roles.Where(r => r.Removed).Select(r => $"<@&{r.RoleId}>").ToList();

        var embed = (added.Count, removed.Count) switch
        {
            (0, 0) => null,
            (_, 0) => Titled(added.Count == 1 ? "🎭 Rôle ajouté" : "🎭 Rôles ajoutés", Color.Blue),
            (0, _) => Titled(removed.Count == 1 ? "🎭 Rôle retiré" : "🎭 Rôles retirés", Color.LightGrey),
            _ => Titled("🎭 Rôles modifiés", Color.Blue),
        };
        if (added.Count > 0)
            embed?.AddField("Ajouté", string.Join(' ', added), inline: true);
        if (removed.Count > 0)
            embed?.AddField("Retiré", string.Join(' ', removed), inline: true);
        return embed;
    }

    /// <summary>L'auteur n'est fourni que s'il est en cache : sinon, on le retrouve dans l'audit log via l'API REST.</summary>
    private static async Task<IUser?> GetExecutorAsync(SocketAuditLogEntry entry, SocketGuild guild)
    {
        if (entry.User is not null)
            return entry.User;

        var recent = await guild.GetAuditLogsAsync(10, actionType: entry.Action).FlattenAsync();
        return recent.FirstOrDefault(e => e.Id == entry.Id)?.User;
    }

    private static EmbedBuilder Titled(string title, Color color) => new EmbedBuilder().WithTitle(title).WithColor(color);

    private static string DisplayName(IGuildUser? member, IUser? user) =>
        member?.DisplayName ?? user?.GlobalName ?? user?.Username ?? "Utilisateur inconnu";
}
