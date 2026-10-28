using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;

namespace SianaBot.ReactionRoles;

/// <summary>Clics sur les boutons des panneaux de rôles (voir <see cref="RolePanel"/>).</summary>
// BotService crée des SocketInteractionContext non typés : le module générique sur SocketMessageComponent n'aurait pas de contexte.
public sealed class RolePanelModule(DiscordRestClient rest, ReactionRoleService service)
    : InteractionModuleBase<SocketInteractionContext>
{
    [ComponentInteraction(RolePanel.CustomIdPrefix + "*:*")]
    public async Task OnClickAsync(string mode, ulong roleId)
    {
        await DeferAsync(ephemeral: true);

        // Seuls les rôles affichés sur le panneau peuvent être pris.
        var panelRoles = Context.Interaction is SocketMessageComponent click ? RolePanel.RoleIds(click.Message) : [];
        if (Context.Guild is null || !panelRoles.Contains(roleId))
        {
            await FollowupAsync("❌ Ce bouton n'est plus valide.", ephemeral: true);
            return;
        }

        var member = await rest.GetGuildUserAsync(Context.Guild.Id, Context.User.Id);
        var exclusiveWith = mode == RolePanel.Exclusive ? panelRoles : [];
        var result = await service.ToggleAsync(member, roleId, exclusiveWith);

        var role = $"<@&{roleId}>";
        var message = result.Outcome switch
        {
            ToggleOutcome.Added when result.Replaced.Count > 0 =>
                $"✅ Tu as maintenant {role} (à la place de {string.Join(", ", result.Replaced.Select(r => $"<@&{r}>"))}).",
            ToggleOutcome.Added => $"✅ Tu as maintenant {role}.",
            ToggleOutcome.Removed => $"✅ {role} retiré.",
            ToggleOutcome.Pending =>
                $"⏳ {role} est réservé aux pseudos « Prénom N. » : ton choix sera activé dès que ton pseudo sera conforme.",
            _ => $"✅ Choix {role} annulé.",
        };
        await FollowupAsync(message, ephemeral: true, allowedMentions: AllowedMentions.None);
    }
}
