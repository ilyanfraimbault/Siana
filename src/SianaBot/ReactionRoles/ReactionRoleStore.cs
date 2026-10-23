using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SianaBot.ReactionRoles;

/// <summary>
/// Persistance JSON simple. Les lectures se font sur un snapshot immuable, les écritures sont sérialisées.
/// Le fichier est relu s'il a changé sur le disque : la CLI (<c>siana roles ...</c>) l'écrit depuis un autre process.
/// </summary>
public sealed class ReactionRoleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Lock _reloadLock = new();
    private volatile IReadOnlyList<ReactionRole> _items = [];
    private DateTime _loadedWriteTime = DateTime.MinValue;

    public ReactionRoleStore(IOptions<ReactionRoleOptions> options)
    {
        _path = Path.GetFullPath(options.Value.StoragePath);
        RefreshIfChanged();
    }

    public IReadOnlyList<ReactionRole> All()
    {
        RefreshIfChanged();
        return _items;
    }

    public ReactionRole? Find(ulong messageId, string emoteKey) =>
        All().FirstOrDefault(r => r.MessageId == messageId && r.EmoteKey == emoteKey);

    public IReadOnlyList<ReactionRole> ForGuild(ulong guildId) =>
        All().Where(r => r.GuildId == guildId).ToList();

    public Task UpsertAsync(ReactionRole reactionRole) => UpsertManyAsync([reactionRole]);

    public Task UpsertManyAsync(IReadOnlyCollection<ReactionRole> reactionRoles) =>
        MutateAsync(items =>
        {
            foreach (var reactionRole in reactionRoles)
            {
                items.RemoveAll(r => r.MessageId == reactionRole.MessageId && r.EmoteKey == reactionRole.EmoteKey);
                items.Add(reactionRole);
            }
            return true;
        });

    public Task<bool> RemoveAsync(ulong messageId, string emoteKey) =>
        MutateAsync(items => items.RemoveAll(r => r.MessageId == messageId && r.EmoteKey == emoteKey) > 0);

    private async Task<bool> MutateAsync(Func<List<ReactionRole>, bool> mutate)
    {
        await _writeLock.WaitAsync();
        try
        {
            RefreshIfChanged();
            var items = _items.ToList();
            if (!mutate(items))
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(items, JsonOptions));
            File.Move(tmp, _path, overwrite: true);

            lock (_reloadLock)
            {
                _items = items;
                _loadedWriteTime = File.GetLastWriteTimeUtc(_path);
            }
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void RefreshIfChanged()
    {
        var writeTime = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
        if (writeTime == _loadedWriteTime)
            return;

        lock (_reloadLock)
        {
            if (writeTime == _loadedWriteTime)
                return;

            _items = writeTime == DateTime.MinValue
                ? []
                : JsonSerializer.Deserialize<List<ReactionRole>>(File.ReadAllText(_path), JsonOptions) ?? [];
            _loadedWriteTime = writeTime;
        }
    }
}
