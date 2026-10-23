using Discord;

namespace SianaBot.ReactionRoles;

/// <param name="EmoteKey">Clé de comparaison (voir <see cref="Emotes.Key"/>).</param>
/// <param name="Emote">Représentation affichable (😀 ou &lt;:nom:id&gt;).</param>
public sealed record ReactionRole(ulong GuildId, ulong ChannelId, ulong MessageId, string EmoteKey, string Emote, ulong RoleId)
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
