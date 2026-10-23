using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Discord;

namespace SianaBot.ReactionRoles;

public enum ReactionRoleMode
{
    /// <summary>Le rôle suit la réaction : réagir le donne, retirer sa réaction l'enlève.</summary>
    Persistent = 0,

    /// <summary>Chaque clic inverse le rôle, et le bot retire aussitôt la réaction.</summary>
    Toggle = 1,
}

/// <param name="EmoteKey">Clé de comparaison (voir <see cref="Emotes.Key"/>).</param>
/// <param name="Emote">Représentation affichable (😀 ou &lt;:nom:id&gt;).</param>
public sealed record ReactionRole(
    ulong GuildId,
    ulong ChannelId,
    ulong MessageId,
    string EmoteKey,
    string Emote,
    ulong RoleId,
    ReactionRoleMode Mode = ReactionRoleMode.Persistent)
{
    public string JumpUrl => $"https://discord.com/channels/{GuildId}/{ChannelId}/{MessageId}";
}

public sealed class ReactionRoleOptions
{
    public const string Section = "ReactionRoles";

    public string StoragePath { get; set; } = "data/reaction-roles.json";
}

internal static class Emotes
{
    /// <summary>Id pour un emoji custom, sinon le caractère unicode sans variation selector (U+FE0F).</summary>
    public static string Key(IEmote emote) =>
        emote is Emote custom ? custom.Id.ToString() : emote.Name.Replace("️", "");

    /// <summary>Discord valide l'emoji unicode lui-même quand le bot ajoute la réaction.</summary>
    public static IEmote Parse(string input)
    {
        input = input.Trim();
        return Emote.TryParse(input, out var custom) ? custom : new Emoji(input);
    }
}

internal static partial class MessageLinks
{
    /// <summary>Lit un lien de message Discord (https://discord.com/channels/guild/channel/message).</summary>
    public static bool TryParse(string input, out ulong guildId, out ulong channelId, out ulong messageId)
    {
        var match = MessageLinkRegex().Match(input.Trim());
        guildId = channelId = messageId = 0;
        return match.Success
            && ulong.TryParse(match.Groups[1].Value, out guildId)
            && ulong.TryParse(match.Groups[2].Value, out channelId)
            && ulong.TryParse(match.Groups[3].Value, out messageId);
    }

    [GeneratedRegex(@"channels/(\d+)/(\d+)/(\d+)")]
    private static partial Regex MessageLinkRegex();
}
