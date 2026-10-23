using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SianaBot.ReactionRoles;

/// <summary>Persistance JSON simple. Les lectures se font sur un snapshot immuable, les écritures sont sérialisées.</summary>
public sealed class ReactionRoleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private volatile IReadOnlyList<ReactionRole> _items;

    public ReactionRoleStore(IOptions<ReactionRoleOptions> options)
    {
        _path = Path.GetFullPath(options.Value.StoragePath);
        _items = File.Exists(_path)
            ? JsonSerializer.Deserialize<List<ReactionRole>>(File.ReadAllText(_path), JsonOptions) ?? []
            : [];
    }

    public ReactionRole? Find(ulong messageId, string emoteKey) =>
        _items.FirstOrDefault(r => r.MessageId == messageId && r.EmoteKey == emoteKey);

    public IReadOnlyList<ReactionRole> ForGuild(ulong guildId) =>
        _items.Where(r => r.GuildId == guildId).ToList();

    public Task UpsertAsync(ReactionRole reactionRole) =>
        MutateAsync(items =>
        {
            items.RemoveAll(r => r.MessageId == reactionRole.MessageId && r.EmoteKey == reactionRole.EmoteKey);
            items.Add(reactionRole);
            return true;
        });

    public Task<bool> RemoveAsync(ulong messageId, string emoteKey) =>
        MutateAsync(items => items.RemoveAll(r => r.MessageId == messageId && r.EmoteKey == emoteKey) > 0);

    private async Task<bool> MutateAsync(Func<List<ReactionRole>, bool> mutate)
    {
        await _writeLock.WaitAsync();
        try
        {
            var items = _items.ToList();
            if (!mutate(items))
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(items, JsonOptions));
            File.Move(tmp, _path, overwrite: true);

            _items = items;
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
