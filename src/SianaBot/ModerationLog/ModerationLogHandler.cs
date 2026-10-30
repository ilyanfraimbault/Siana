using Discord;
using Discord.WebSocket;
using static SianaBot.ModerationLog.ModerationLogChannel;

namespace SianaBot.ModerationLog;

/// <summary>
/// Journalise les actions de modération (ban, kick, timeout, mute vocal), les changements de pseudo et de rôles,
/// d'après l'audit log du serveur : elles sont prises en compte qu'elles viennent de Discord, d'un bot ou de Siana.
/// </summary>
public sealed class ModerationLogHandler(
    DiscordSocketClient client,
    ModerationLogChannel channel,
    ILogger<ModerationLogHandler> logger)
{
    public void Attach()
    {
        if (channel.GuildId is null)
            return;

        // Hors de la gateway : l'envoi (et l'éventuel appel REST pour l'auteur) ne doit pas bloquer la réception des events.
        client.AuditLogCreated += (entry, guild) =>
        {
            _ = Task.Run(() => OnAuditLogCreatedAsync(entry, guild));
            return Task.CompletedTask;
        };
    }

    private async Task OnAuditLogCreatedAsync(SocketAuditLogEntry entry, SocketGuild guild)
    {
        if (channel.Watched(guild.Id) is null)
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
            About(embed, await target.GetOrDownloadAsync(), guild, target.Id).WithTimestamp(entry.CreatedAt);

            if (!string.IsNullOrWhiteSpace(entry.Reason))
                embed.AddField("Raison", entry.Reason);

            if (await GetExecutorAsync(entry, guild) is { } executor)
            {
                var executorMember = guild.GetUser(executor.Id);
                embed.WithFooter(DisplayName(executorMember, executor),
                    executorMember?.GetDisplayAvatarUrl() ?? executor.GetDisplayAvatarUrl());
            }

            await channel.SendAsync(embed);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Log de modération non envoyé ({Action} sur {UserId})", entry.Action, target.Id);
        }
    }

    /// <summary>Timeout, mute / sourdine vocale et pseudo sur le serveur.</summary>
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

        if (before.Nickname != after.Nickname)
            return Titled("📝 Pseudo modifié", Color.Teal)
                .AddField("Avant", before.Nickname ?? "*aucun*", inline: true)
                .AddField("Après", after.Nickname ?? "*aucun*", inline: true);

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
}
