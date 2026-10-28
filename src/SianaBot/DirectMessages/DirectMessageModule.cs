using Discord;
using Discord.Interactions;
using Discord.Net;

namespace SianaBot.DirectMessages;

/// <summary>
/// Envoie un message privé au nom du bot. Réservé par défaut aux administrateurs : le serveur peut l'ouvrir
/// à d'autres rôles dans Paramètres du serveur → Intégrations.
/// </summary>
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public sealed class DirectMessageModule(ILogger<DirectMessageModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("mp", "Envoie un message privé à un membre de la part du bot")]
    public async Task SendAsync(
        [Summary("user", "Destinataire")] IUser user,
        [Summary("message", "Contenu du message"), MaxLength(DiscordConfig.MaxMessageSize)] string message)
    {
        if (user.IsBot)
        {
            await RespondAsync("❌ Impossible d'envoyer un message privé à un bot.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            await user.SendMessageAsync(message, allowedMentions: AllowedMentions.None);
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
        {
            await FollowupAsync($"❌ {user.Mention} n'accepte pas les messages privés du bot.",
                ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        logger.LogInformation("MP envoyé par {Sender} ({SenderId}) à {Recipient} ({RecipientId})",
            Context.User.Username, Context.User.Id, user.Username, user.Id);

        await FollowupAsync($"✅ Message envoyé à {user.Mention}.", ephemeral: true, allowedMentions: AllowedMentions.None);
    }
}
