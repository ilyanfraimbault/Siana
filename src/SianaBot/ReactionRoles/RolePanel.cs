using Discord;

namespace SianaBot.ReactionRoles;

public sealed record PanelEntry(IEmote Emote, ulong RoleId, string Label);

/// <summary>
/// Panneaux de rôles à boutons. Sans état côté bot : chaque bouton porte son rôle dans son custom id
/// (<c>rolepanel:&lt;mode&gt;:&lt;id du rôle&gt;</c>), et les autres rôles du panneau se lisent sur le message.
/// </summary>
internal static class RolePanel
{
    public const string CustomIdPrefix = "rolepanel:";

    /// <summary>Mode exclusif : prendre un rôle du panneau retire les autres.</summary>
    public const string Exclusive = "x";

    public const string Toggle = "t";

    public static readonly Color DefaultColor = new(0x8B5CF6);

    public static (Embed Embed, MessageComponent Components) Build(
        string? title, string? description, Color color, IReadOnlyList<PanelEntry> entries, bool exclusive)
    {
        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(color)
            .WithFooter(exclusive
                ? "Un seul choix possible : en prendre un retire l'autre. Reclique pour le retirer."
                : "Clique pour prendre un rôle, reclique pour le retirer.")
            .Build();

        var components = new ComponentBuilder();
        foreach (var entry in entries)
            components.WithButton(entry.Label, CustomId(entry.RoleId, exclusive), ButtonStyle.Secondary, entry.Emote);

        return (embed, components.Build());
    }

    /// <summary>Rôles des boutons de panneau présents sur le message.</summary>
    public static IReadOnlyList<ulong> RoleIds(IMessage message) =>
        message.Components
            .OfType<ActionRowComponent>()
            .SelectMany(row => row.Components)
            .OfType<ButtonComponent>()
            .Select(button => button.CustomId?.Split(':'))
            .Where(parts => parts is [_, _, _] && $"{parts[0]}:" == CustomIdPrefix)
            .Select(parts => ulong.TryParse(parts![2], out var roleId) ? roleId : 0)
            .Where(roleId => roleId != 0)
            .ToList();

    private static string CustomId(ulong roleId, bool exclusive) =>
        $"{CustomIdPrefix}{(exclusive ? Exclusive : Toggle)}:{roleId}";
}
