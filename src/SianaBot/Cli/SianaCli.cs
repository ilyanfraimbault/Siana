using Discord;
using Discord.Net;
using Discord.Rest;
using Microsoft.Extensions.Options;
using SianaBot.NicknameConvention;
using SianaBot.ReactionRoles;

namespace SianaBot.Cli;

/// <summary>
/// Commandes d'administration, lancées dans le conteneur de prod : <c>docker exec siana-bot siana &lt;commande&gt;</c>.
/// Elles passent par l'API REST avec le token du bot, à côté du process principal qui garde la gateway.
/// </summary>
public static class SianaCli
{
    private const string Usage = """
        Usage : siana <commande>

          roles list
              Liste les reaction roles suivis.
          roles sync
              Rattrape les réactions posées pendant une coupure (le bot le fait aussi à chaque démarrage).
          roles grant --message <lien> --emoji <emoji> --role <id>
              Donne le rôle à tous ceux qui ont réagi avec cet emoji sur un message non suivi.
          roles panel --channel <id> --title <titre> [--description <texte>] [--color <#RRGGBB>]
                      --entry "<emoji>|<id du rôle>|<libellé>" [--entry ...]
              Publie un embed de choix de rôles : un clic donne le rôle, un second le retire.
              "\n" dans la description fait un retour à la ligne.
          nick check <pseudo>
              Teste un pseudo contre la convention « Prénom N. ».
          nick sync
              Applique la convention de pseudo à tous les membres.
        """;

    private static readonly Color DefaultColor = new(0x8B5CF6);

    public static bool Handles(string[] args) => args.Length > 0 && args[0] is "roles" or "nick" or "help" or "--help";

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var command = CliCommand.Parse(args);
            if (command.Verb is "help" or "--help")
            {
                Console.WriteLine(Usage);
                return 0;
            }
            if (command is { Verb: "nick", Action: "check" })
                return CheckNickname(command);

            var builder = Host.CreateApplicationBuilder();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Services.AddSianaCore(builder.Configuration);
            builder.Services.AddSingleton(new DiscordRestClient(new DiscordRestConfig { LogLevel = LogSeverity.Warning }));
            using var host = builder.Build();

            var token = host.Services.GetRequiredService<IOptions<DiscordOptions>>().Value.Token;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Discord:Token manquant.");

            var rest = host.Services.GetRequiredService<DiscordRestClient>();
            await rest.LoginAsync(TokenType.Bot, token);
            return await ExecuteAsync(command, host.Services);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or HttpException or FormatException)
        {
            Console.Error.WriteLine($"Erreur : {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ExecuteAsync(CliCommand command, IServiceProvider services)
    {
        var reactionRoles = services.GetRequiredService<ReactionRoleService>();
        switch (command.Verb, command.Action)
        {
            case ("roles", "list"):
                var items = services.GetRequiredService<ReactionRoleStore>().All();
                foreach (var r in items)
                    Console.WriteLine($"{r.Emote}  rôle {r.RoleId}  {r.Mode}  {r.JumpUrl}");
                Console.WriteLine($"{items.Count} reaction role(s).");
                return 0;

            case ("roles", "sync"):
                Console.WriteLine($"{await reactionRoles.SyncAsync()} réaction(s) rattrapée(s).");
                return 0;

            case ("roles", "grant"):
                if (!MessageLinks.TryParse(command.Required("message"), out _, out var channelId, out var messageId))
                    throw new ArgumentException("--message doit être un lien de message Discord.");
                var granted = await reactionRoles.GrantFromReactionsAsync(
                    channelId, messageId, Emotes.Parse(command.Required("emoji")), command.RequiredId("role"));
                Console.WriteLine($"{granted} rôle(s) ajouté(s).");
                return 0;

            case ("roles", "panel"):
                var entries = command.All("entry").Select(ParseEntry).ToList();
                if (entries.Count == 0)
                    throw new ArgumentException("Il faut au moins une option --entry.");
                var message = await reactionRoles.CreatePanelAsync(
                    command.RequiredId("channel"),
                    command.Required("title"),
                    command.Optional("description")?.Replace("\\n", "\n"),
                    command.Optional("color") is { } hex ? new Color(Convert.ToUInt32(hex.TrimStart('#'), 16)) : DefaultColor,
                    entries);
                Console.WriteLine($"Panneau publié : {message.GetJumpUrl()}");
                return 0;

            case ("nick", "sync"):
                if (!services.GetRequiredService<IOptions<NicknameConventionOptions>>().Value.Enabled)
                    throw new InvalidOperationException("Convention de pseudo désactivée (NicknameConvention:GuildId / RoleId).");
                var (added, removed) = await services.GetRequiredService<NicknameConventionService>().SyncAsync();
                Console.WriteLine($"{added} rôle(s) ajouté(s), {removed} retiré(s).");
                return 0;

            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    private static int CheckNickname(CliCommand command)
    {
        var name = string.Join(' ', command.Positional);
        var matches = NicknameConventionService.Matches(name);
        Console.WriteLine(matches ? $"✅ « {name} » respecte la convention." : $"❌ « {name} » ne respecte pas la convention.");
        return matches ? 0 : 1;
    }

    private static PanelEntry ParseEntry(string value)
    {
        var parts = value.Split('|', 3, StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !ulong.TryParse(parts[1], out var roleId))
            throw new ArgumentException($"--entry invalide : « {value} » (attendu \"<emoji>|<id du rôle>|<libellé>\").");
        return new PanelEntry(Emotes.Parse(parts[0]), roleId, parts[2]);
    }
}

internal sealed record CliCommand(string Verb, string? Action, IReadOnlyList<string> Positional, ILookup<string, string> Options)
{
    public static CliCommand Parse(string[] args)
    {
        var positional = new List<string>();
        var options = new List<(string Key, string Value)>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--") || args[i] == "--help")
            {
                positional.Add(args[i]);
                continue;
            }
            if (i + 1 >= args.Length)
                throw new ArgumentException($"Valeur manquante pour {args[i]}.");
            options.Add((args[i][2..], args[++i]));
        }

        return new CliCommand(
            positional[0], positional.ElementAtOrDefault(1), positional.Skip(2).ToList(), options.ToLookup(o => o.Key, o => o.Value));
    }

    public IEnumerable<string> All(string name) => Options[name];

    public string? Optional(string name) => Options[name].LastOrDefault();

    public string Required(string name) => Optional(name) ?? throw new ArgumentException($"Option --{name} manquante.");

    public ulong RequiredId(string name) =>
        ulong.TryParse(Required(name), out var id) ? id : throw new ArgumentException($"--{name} doit être un identifiant Discord.");
}
