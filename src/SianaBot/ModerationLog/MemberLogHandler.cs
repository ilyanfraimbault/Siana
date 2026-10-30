using Discord;
using Discord.WebSocket;
using static SianaBot.ModerationLog.ModerationLogChannel;

namespace SianaBot.ModerationLog;

/// <summary>
/// Journalise les arrivées et départs de membres, et les changements de profil Discord (nom d'utilisateur, nom affiché, avatar).
/// Le pseudo propre au serveur passe par l'audit log (<see cref="ModerationLogHandler"/>).
/// </summary>
public sealed class MemberLogHandler(
    DiscordSocketClient client,
    ModerationLogChannel channel,
    ILogger<MemberLogHandler> logger)
{
    public void Attach()
    {
        if (channel.GuildId is null)
            return;

        client.UserJoined += member => Run(() => OnJoinedAsync(member), member.Id);
        client.UserLeft += (guild, user) => Run(() => OnLeftAsync(guild, user), user.Id);
        client.UserUpdated += (before, after) => Run(() => OnUserUpdatedAsync(before, after), after.Id);
    }

    private Task Run(Func<Task> handler, ulong userId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Log de membre non envoyé ({UserId})", userId);
            }
        });
        return Task.CompletedTask;
    }

    private async Task OnJoinedAsync(SocketGuildUser member)
    {
        if (channel.Watched(member.Guild.Id) is not { } guild)
            return;

        var created = member.CreatedAt.ToUnixTimeSeconds();
        var embed = About(Titled("📥 Membre arrivé", Color.Green), member, guild, member.Id)
            .AddField("Compte créé", $"<t:{created}:D> (<t:{created}:R>)", inline: true)
            .AddField("Membres", guild.MemberCount.ToString(), inline: true)
            .WithCurrentTimestamp();
        By(embed, member, guild);
        await channel.SendAsync(embed);
    }

    private async Task OnLeftAsync(SocketGuild source, SocketUser user)
    {
        if (channel.Watched(source.Id) is not { } guild)
            return;

        var embed = By(About(Titled("📤 Membre parti", Color.LightGrey), user, guild, user.Id), user, guild).WithCurrentTimestamp();
        if (user is SocketGuildUser { JoinedAt: { } joined })
            embed.AddField("Arrivé", $"<t:{joined.ToUnixTimeSeconds()}:R>", inline: true);
        if (user is SocketGuildUser { Roles.Count: > 1 } member)
            embed.AddField("Rôles", Truncate(string.Join(' ', member.Roles.Where(r => !r.IsEveryone).Select(r => r.Mention))));
        await channel.SendAsync(embed);
    }

    private async Task OnUserUpdatedAsync(SocketUser before, SocketUser after)
    {
        if (after.IsBot || channel.Watched(channel.GuildId) is not { } guild || guild.GetUser(after.Id) is null)
            return;

        var embed = By(About(Titled("🪪 Profil modifié", Color.Teal), after, guild, after.Id), after, guild).WithCurrentTimestamp();
        if (before.Username != after.Username)
            embed.AddField("Nom d'utilisateur", $"{before.Username} → {after.Username}");
        if (before.GlobalName != after.GlobalName)
            embed.AddField("Nom affiché", $"{before.GlobalName ?? "*aucun*"} → {after.GlobalName ?? "*aucun*"}");
        if (before.AvatarId != after.AvatarId)
            embed.AddField("Avatar", "modifié (nouvel avatar en miniature)");

        if (embed.Fields.Count > 0)
            await channel.SendAsync(embed);
    }
}
