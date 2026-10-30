using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using static SianaBot.ModerationLog.ModerationLogChannel;

namespace SianaBot.ModerationLog;

/// <summary>Journalise chaque slash command du bot utilisée sur le serveur, avec ses arguments et son résultat.</summary>
public sealed class CommandLogHandler(
    InteractionService interactions,
    ModerationLogChannel channel,
    ILogger<CommandLogHandler> logger)
{
    public void Attach()
    {
        if (channel.GuildId is null)
            return;

        interactions.SlashCommandExecuted += (command, context, result) =>
        {
            _ = Task.Run(() => OnExecutedAsync(context, result));
            return Task.CompletedTask;
        };
    }

    private async Task OnExecutedAsync(IInteractionContext context, IResult result)
    {
        if (context.Interaction is not SocketSlashCommand command || channel.Watched(context.Guild?.Id) is not { } guild)
            return;

        try
        {
            var embed = About(Titled("⌨️ Commande utilisée", result.IsSuccess ? Color.Purple : Color.DarkGrey), command.User, guild, command.User.Id)
                .AddField("Commande", Truncate(Format(command.Data)))
                .AddField("Salon", $"<#{command.ChannelId}>", inline: true)
                .WithTimestamp(command.CreatedAt);
            By(embed, command.User, guild);
            if (!result.IsSuccess)
                embed.AddField("Résultat", Truncate($"❌ {result.Error} : {result.ErrorReason}"), inline: true);

            await channel.SendAsync(embed);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Log de commande non envoyé (/{Command} par {UserId})", command.Data.Name, command.User.Id);
        }
    }

    /// <summary><c>/groupe sous-commande nom:valeur …</c>, les membres, rôles et salons sous forme de mention.</summary>
    private static string Format(SocketSlashCommandData data)
    {
        var parts = new List<string> { $"/{data.Name}" };
        var options = data.Options;
        while (options.Count == 1 && options.First() is { Type: ApplicationCommandOptionType.SubCommand or ApplicationCommandOptionType.SubCommandGroup } sub)
        {
            parts.Add(sub.Name);
            options = sub.Options;
        }
        parts.AddRange(options.Select(o => $"{o.Name}:{FormatValue(o.Value)}"));
        return string.Join(' ', parts);
    }

    private static string FormatValue(object? value) => value switch
    {
        IUser user => user.Mention,
        IRole role => role.Mention,
        IChannel channel => $"<#{channel.Id}>",
        IAttachment attachment => $"[{attachment.Filename}]({attachment.Url})",
        string text => $"`{text.Replace('`', '\'')}`",
        _ => $"`{value}`",
    };
}
