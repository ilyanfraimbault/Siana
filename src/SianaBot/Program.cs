using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using SianaBot;
using SianaBot.Cli;
using SianaBot.ModerationLog;
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
// GuildBans correspond à l'intent Guild Moderation (non privilégié), qui porte les entrées d'audit log. Les logs de messages
// demandent aussi Message Content, et ceux des arrivées / départs / profils Server Members : deux intents privilégiés.
var moderationLog = builder.Configuration.GetSection(ModerationLogOptions.Section).Get<ModerationLogOptions>();
if (moderationLog?.Enabled == true)
    intents |= GatewayIntents.GuildBans | GatewayIntents.GuildMembers | GatewayIntents.GuildMessages | GatewayIntents.MessageContent;

builder.Services.AddSingleton(new DiscordSocketConfig
{
    GatewayIntents = intents,
    LogLevel = LogSeverity.Info,
    // Messages gardés par salon, pour journaliser le contenu d'origine d'un message modifié ou supprimé.
    MessageCacheSize = moderationLog?.Enabled == true ? 200 : 0,
});
builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton<DiscordRestClient>(sp => sp.GetRequiredService<DiscordSocketClient>().Rest);
builder.Services.AddSingleton(sp => new InteractionService(sp.GetRequiredService<DiscordSocketClient>()));

builder.Services.AddSingleton<ReactionRoleHandler>();
builder.Services.AddSingleton<NicknameConventionHandler>();
builder.Services.AddSingleton<ModerationLogChannel>();
builder.Services.AddSingleton<ModerationLogHandler>();
builder.Services.AddSingleton<MessageLogHandler>();
builder.Services.AddSingleton<CommandLogHandler>();
builder.Services.AddSingleton<MemberLogHandler>();

builder.Services.AddHostedService<BotService>();

builder.Build().Run();
return 0;
