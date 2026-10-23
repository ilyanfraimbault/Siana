using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using SianaBot;
using SianaBot.Cli;
using SianaBot.NicknameConvention;
using SianaBot.ReactionRoles;

if (SianaCli.Handles(args))
    return await SianaCli.RunAsync(args);

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSianaCore(builder.Configuration);

// GuildMembers est un intent privilégié (à activer dans le Developer Portal) : on ne le demande
// que si la convention de pseudo est configurée, sinon Discord refuserait la connexion.
var nicknameConvention = builder.Configuration.GetSection(NicknameConventionOptions.Section).Get<NicknameConventionOptions>();
var intents = GatewayIntents.Guilds | GatewayIntents.GuildMessageReactions;
if (nicknameConvention?.Enabled == true)
    intents |= GatewayIntents.GuildMembers;

builder.Services.AddSingleton(new DiscordSocketConfig
{
    GatewayIntents = intents,
    LogLevel = LogSeverity.Info,
});
builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton<DiscordRestClient>(sp => sp.GetRequiredService<DiscordSocketClient>().Rest);
builder.Services.AddSingleton(sp => new InteractionService(sp.GetRequiredService<DiscordSocketClient>()));

builder.Services.AddSingleton<ReactionRoleHandler>();
builder.Services.AddSingleton<NicknameConventionHandler>();

builder.Services.AddHostedService<BotService>();

builder.Build().Run();
return 0;
