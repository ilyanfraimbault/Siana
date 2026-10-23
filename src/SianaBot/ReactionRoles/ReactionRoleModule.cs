using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Discord.Interactions;
using Discord.Net;

namespace SianaBot.ReactionRoles;

[Group("reactionrole", "Rôles attribués par réaction")]
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.ManageRoles)]
public sealed partial class ReactionRoleModule(ReactionRoleStore store) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("add", "Associe un emoji sur un message à un rôle")]
    public async Task AddAsync(
        [Summary("message", "ID ou lien du message")] string message,
        [Summary("emoji", "Emoji de la réaction")] string emoji,
        [Summary("role", "Rôle à attribuer")] IRole role)
    {
        var bot = Context.Guild.CurrentUser;
        if (!bot.GuildPermissions.ManageRoles)
        {
            await RespondAsync("❌ Il me faut la permission **Gérer les rôles**.", ephemeral: true);
            return;
        }
        if (role.IsManaged || role.Id == Context.Guild.EveryoneRole.Id)
        {
            await RespondAsync("❌ Ce rôle ne peut pas être attribué manuellement.", ephemeral: true);
            return;
        }
        if (role.Position >= bot.Hierarchy)
        {
            await RespondAsync($"❌ Mon rôle doit être placé au-dessus de {role.Mention} dans les paramètres du serveur.",
                ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        await DeferAsync(ephemeral: true);

        var target = await ResolveMessageAsync(message);
        if (target is null)
        {
            await FollowupAsync("❌ Message introuvable. Donne son ID (dans ce salon) ou son lien.", ephemeral: true);
            return;
        }

        var emote = Emotes.Parse(emoji);
        try
        {
            await target.AddReactionAsync(emote);
        }
        catch (HttpException)
        {
            await FollowupAsync("❌ Impossible d'ajouter cette réaction (emoji invalide, ou emoji d'un autre serveur ?).", ephemeral: true);
            return;
        }

        await store.UpsertAsync(new ReactionRole(
            Context.Guild.Id, target.Channel.Id, target.Id, Emotes.Key(emote), emote.ToString()!, role.Id));

        await FollowupAsync($"✅ Réagir avec {emote} sur {target.GetJumpUrl()} donnera le rôle {role.Mention}.",
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [SlashCommand("remove", "Supprime l'association d'un emoji sur un message")]
    public async Task RemoveAsync(
        [Summary("message", "ID ou lien du message")] string message,
        [Summary("emoji", "Emoji de la réaction")] string emoji)
    {
        var emote = Emotes.Parse(emoji);
        if (!TryParseMessageReference(message, out var channelId, out var messageId)
            || !await store.RemoveAsync(messageId, Emotes.Key(emote)))
        {
            await RespondAsync("❌ Aucun reaction role correspondant.", ephemeral: true);
            return;
        }

        await RespondAsync($"✅ Reaction role {emote} supprimé.", ephemeral: true);

        // Best effort : on retire aussi la réaction du bot.
        if (Context.Guild.GetChannel(channelId) is IMessageChannel channel
            && await channel.GetMessageAsync(messageId) is IUserMessage target)
        {
            try { await target.RemoveReactionAsync(emote, Context.Client.CurrentUser); }
            catch (HttpException) { }
        }
    }

    [SlashCommand("list", "Liste les reaction roles du serveur")]
    public async Task ListAsync()
    {
        var items = store.ForGuild(Context.Guild.Id);
        if (items.Count == 0)
        {
            await RespondAsync("Aucun reaction role configuré.", ephemeral: true);
            return;
        }

        var sb = new StringBuilder();
        foreach (var r in items)
            sb.AppendLine($"{r.Emote} → <@&{r.RoleId}> · {r.JumpUrl}");

        await RespondAsync(sb.ToString(), ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private async Task<IUserMessage?> ResolveMessageAsync(string reference)
    {
        if (!TryParseMessageReference(reference, out var channelId, out var messageId))
            return null;
        if (Context.Guild.GetChannel(channelId) is not IMessageChannel channel)
            return null;

        try { return await channel.GetMessageAsync(messageId) as IUserMessage; }
        catch (HttpException) { return null; }
    }

    /// <summary>Accepte un lien de message de ce serveur, ou un ID de message du salon courant.</summary>
    private bool TryParseMessageReference(string reference, out ulong channelId, out ulong messageId)
    {
        reference = reference.Trim();
        var match = MessageLinkRegex().Match(reference);
        if (match.Success && ulong.Parse(match.Groups[1].Value) == Context.Guild.Id)
        {
            channelId = ulong.Parse(match.Groups[2].Value);
            messageId = ulong.Parse(match.Groups[3].Value);
            return true;
        }

        channelId = Context.Channel.Id;
        return ulong.TryParse(reference, out messageId);
    }

    [GeneratedRegex(@"channels/(\d+)/(\d+)/(\d+)")]
    private static partial Regex MessageLinkRegex();
}
