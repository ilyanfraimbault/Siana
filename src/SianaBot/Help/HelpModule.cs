using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using SianaBot.Memes;

namespace SianaBot.Help;

/// <summary>Liste les commandes du bot que le membre peut utiliser : celles des autres rôles ne sont pas mentionnées.</summary>
[CommandContextType(InteractionContextType.Guild)]
public sealed class HelpModule(IOptions<MemeOptions> memeOptions) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("help", "Liste les commandes que tu peux utiliser")]
    public async Task HelpAsync()
    {
        var user = (SocketGuildUser)Context.User;
        var embed = new EmbedBuilder()
            .WithTitle("Commandes de Siana")
            .WithColor(Color.Blue)
            .AddField("Tout le monde", "`/help` — cette aide");

        if (memeOptions.Value.HasRole(user))
            embed.AddField(memeOptions.Value.RoleName,
                "`/meme add fichier:<image, GIF ou vidéo>` — ajoute des memes à la banque (jusqu'à 10 à la fois)\n"
                + $"`/meme random` — poste un meme au hasard dans #{memeOptions.Value.ChannelName}\n"
                + "Les memes sont postés en spoiler et anonymes : ni l'auteur ni la personne qui les affiche ne sont indiqués.");

        if (user.GuildPermissions.ManageRoles)
            embed.AddField("Gestion des rôles",
                "`/reactionrole add message:<id ou lien> emoji:<emoji> role:<rôle>` — donne un rôle par réaction\n"
                + "`/reactionrole remove message:<id ou lien> emoji:<emoji>` — supprime un reaction role\n"
                + "`/reactionrole list` — liste les reaction roles");

        if (user.GuildPermissions.Administrator)
            embed.AddField("Administration", "`/mp user:<membre> message:<texte>` — envoie un message privé de la part du bot");

        await RespondAsync(embed: embed.Build(), ephemeral: true);
    }
}
