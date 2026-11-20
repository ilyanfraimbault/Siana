using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Options;

namespace SianaBot.Memes;

/// <summary>
/// Banque de memes réservée au rôle <see cref="MemeOptions.RoleName"/> (MEC PUISSANT). Discord ne permet pas à un bot
/// de masquer une commande selon un rôle : elle est masquée à tous sauf aux administrateurs, et le serveur l'ouvre
/// au rôle dans Paramètres du serveur → Intégrations. <see cref="RequireMemeRoleAttribute"/> vérifie quand même le rôle.
/// </summary>
[Group("meme", "Banque de memes des mecs puissants")]
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[RequireMemeRole]
public sealed class MemeModule(MemeStore store, IOptions<MemeOptions> options, ILogger<MemeModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>
    /// Discord n'a pas d'option « liste de fichiers » : chaque pièce jointe est une option déclarée, les suivantes facultatives.
    /// 10 fichiers par commande, comme dans un message Discord.
    /// </summary>
    [SlashCommand("add", "Ajoute un ou plusieurs memes à la banque (jusqu'à 10 à la fois)")]
    public async Task AddAsync(
        [Summary("fichier", "Image, GIF ou vidéo")] IAttachment fichier,
        [Summary("fichier2", "Meme supplémentaire")] IAttachment? fichier2 = null,
        [Summary("fichier3", "Meme supplémentaire")] IAttachment? fichier3 = null,
        [Summary("fichier4", "Meme supplémentaire")] IAttachment? fichier4 = null,
        [Summary("fichier5", "Meme supplémentaire")] IAttachment? fichier5 = null,
        [Summary("fichier6", "Meme supplémentaire")] IAttachment? fichier6 = null,
        [Summary("fichier7", "Meme supplémentaire")] IAttachment? fichier7 = null,
        [Summary("fichier8", "Meme supplémentaire")] IAttachment? fichier8 = null,
        [Summary("fichier9", "Meme supplémentaire")] IAttachment? fichier9 = null,
        [Summary("fichier10", "Meme supplémentaire")] IAttachment? fichier10 = null)
    {
        await DeferAsync(ephemeral: true);

        IAttachment?[] fichiers = [fichier, fichier2, fichier3, fichier4, fichier5, fichier6, fichier7, fichier8, fichier9, fichier10];
        var added = 0;
        var errors = new List<string>();
        foreach (var attachment in fichiers.OfType<IAttachment>())
        {
            if (await TryAddAsync(attachment) is { } error)
                errors.Add($"`{attachment.Filename}` : {error}");
            else
                added++;
        }

        var lines = new List<string>();
        if (added > 0)
            lines.Add($"✅ {added} meme{(added > 1 ? "s" : "")} ajouté{(added > 1 ? "s" : "")} ! La banque en compte {store.ForGuild(Context.Guild.Id).Count}.");
        lines.AddRange(errors.Select(e => $"❌ {e}"));
        await FollowupAsync(string.Join('\n', lines), ephemeral: true);
    }

    /// <summary>Ajoute un fichier à la banque ; renvoie la raison du refus, ou null s'il est ajouté.</summary>
    private async Task<string?> TryAddAsync(IAttachment attachment)
    {
        var settings = options.Value;
        if (attachment.ContentType is not { } type || !(type.StartsWith("image/") || type.StartsWith("video/")))
            return "ce n'est pas une image, un GIF ou une vidéo.";
        if (attachment.Size > settings.MaxFileSize)
            return $"fichier trop lourd (max {settings.MaxFileSize / (1024 * 1024)} Mo).";

        try
        {
            var meme = await store.AddAsync(attachment.Url, attachment.Filename, Context.Guild.Id, Context.User.Id);
            logger.LogInformation("Meme {File} ajouté par {User} ({UserId})", meme.FileName, Context.User.Username, Context.User.Id);
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Meme : téléchargement de {Url} impossible", attachment.Url);
            return "impossible de récupérer le fichier, réessaie.";
        }
    }

    [SlashCommand("random", "Poste un meme au hasard dans le salon des mecs puissants")]
    public async Task RandomAsync()
    {
        var settings = options.Value;
        var channel = settings.Channel(Context.Guild);
        if (channel is null)
        {
            await RespondAsync($"❌ Salon #{settings.ChannelName} introuvable.", ephemeral: true);
            return;
        }

        var meme = store.PickRandom(Context.Guild.Id);
        if (meme is null)
        {
            await RespondAsync("❌ Aucun meme dans la banque. Ajoutes-en un avec `/meme add`.", ephemeral: true);
            return;
        }

        // Anonyme : le bot poste lui-même le meme (une réponse à la commande afficherait « X a utilisé /meme random »),
        // sans dire qui l'a ajouté ni qui l'a demandé. En spoiler : il faut cliquer pour le voir.
        await DeferAsync(ephemeral: true);
        await channel.SendFileAsync(new FileAttachment(store.PathOf(meme), meme.FileName, isSpoiler: true), "🎲");

        if (Context.Channel.Id == channel.Id)
            await DeleteOriginalResponseAsync();
        else
            await FollowupAsync($"✅ Meme posté dans {channel.Mention}.", ephemeral: true);
    }
}

/// <summary>Réserve les commandes aux membres ayant le rôle configuré dans <see cref="MemeOptions"/>.</summary>
internal sealed class RequireMemeRoleAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<MemeOptions>>().Value;
        return Task.FromResult(context.User is SocketGuildUser user && options.HasRole(user)
            ? PreconditionResult.FromSuccess()
            : PreconditionResult.FromError($"Il te faut le rôle **{options.RoleName}**."));
    }
}
