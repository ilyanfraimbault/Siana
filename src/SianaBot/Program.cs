using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using SianaBot;
using SianaBot.ReactionRoles;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection(DiscordOptions.Section));
builder.Services.Configure<ReactionRoleOptions>(builder.Configuration.GetSection(ReactionRoleOptions.Section));

builder.Services.AddSingleton(new DiscordSocketConfig
{
    // Pas d'intent privilégié : les events de réaction suffisent pour le reaction role.
    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessageReactions,
    LogLevel = LogSeverity.Info,
});
builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton(sp => new InteractionService(sp.GetRequiredService<DiscordSocketClient>()));

builder.Services.AddSingleton<ReactionRoleStore>();
builder.Services.AddSingleton<ReactionRoleHandler>();

builder.Services.AddHostedService<BotService>();

builder.Build().Run();
