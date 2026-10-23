using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using SianaBot.NicknameConvention;
using SianaBot.ReactionRoles;

namespace SianaBot;

public sealed class BotService(
    DiscordSocketClient client,
    InteractionService interactions,
    ReactionRoleHandler reactionRoles,
    NicknameConventionHandler nicknameConvention,
    IServiceProvider services,
    IOptions<DiscordOptions> options,
    ILogger<BotService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var token = options.Value.Token;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "Discord:Token manquant. En dev : dotnet user-secrets set \"Discord:Token\" \"...\". En prod : variable d'env Discord__Token.");

        client.Log += LogAsync;
        interactions.Log += LogAsync;
        client.Ready += OnReadyAsync;
        client.InteractionCreated += OnInteractionCreatedAsync;
        interactions.InteractionExecuted += OnInteractionExecutedAsync;
        reactionRoles.Attach();
        nicknameConvention.Attach();

        await interactions.AddModulesAsync(typeof(BotService).Assembly, services);
        await client.LoginAsync(TokenType.Bot, token);
        await client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await client.StopAsync();
        await client.LogoutAsync();
    }

    private async Task OnReadyAsync()
    {
        if (options.Value.DevGuildId is { } guildId)
            await interactions.RegisterCommandsToGuildAsync(guildId);
        else
            await interactions.RegisterCommandsGloballyAsync();

        logger.LogInformation("Connecté en tant que {User} sur {GuildCount} serveur(s)", client.CurrentUser, client.Guilds.Count);
    }

    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        var context = new SocketInteractionContext(client, interaction);
        await interactions.ExecuteCommandAsync(context, services);
    }

    private async Task OnInteractionExecutedAsync(ICommandInfo command, IInteractionContext context, IResult result)
    {
        if (result.IsSuccess)
            return;

        logger.LogWarning("Commande {Command} échouée : {Error} {Reason}", command?.Name, result.Error, result.ErrorReason);

        var message = result.Error == InteractionCommandError.UnmetPrecondition
            ? $"❌ {result.ErrorReason}"
            : "❌ Une erreur est survenue.";

        if (context.Interaction.HasResponded)
            await context.Interaction.FollowupAsync(message, ephemeral: true);
        else
            await context.Interaction.RespondAsync(message, ephemeral: true);
    }

    private Task LogAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace,
        };
        logger.Log(level, message.Exception, "[{Source}] {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }
}
