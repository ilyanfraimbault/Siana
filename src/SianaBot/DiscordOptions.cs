namespace SianaBot;

public sealed class DiscordOptions
{
    public const string Section = "Discord";

    /// <summary>Token du bot (Developer Portal → Bot → Reset Token).</summary>
    public string Token { get; set; } = "";

    /// <summary>Si renseigné, les slash commands sont enregistrées sur ce serveur uniquement (pratique en dev).</summary>
    public ulong? DevGuildId { get; set; }
}
