using System.Text.Json;

namespace SianaBot.Storage;

/// <summary>
/// Liste persistée dans un fichier JSON. Les lectures se font sur un snapshot immuable, les écritures sont sérialisées.
/// Le fichier est relu s'il a changé sur le disque : la CLI (<c>siana ...</c>) l'écrit depuis un autre process.
/// </summary>
public sealed class JsonListStore<T>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Lock _reloadLock = new();
    private volatile IReadOnlyList<T> _items = [];
    private DateTime _loadedWriteTime = DateTime.MinValue;

    public JsonListStore(string path)
    {
        _path = Path.GetFullPath(path);
        RefreshIfChanged();
    }

    public IReadOnlyList<T> All()
    {
        RefreshIfChanged();
        return _items;
    }

    /// <summary>Applique <paramref name="mutate"/> sur une copie et l'écrit si elle renvoie true.</summary>
    public async Task<bool> MutateAsync(Func<List<T>, bool> mutate)
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
                : JsonSerializer.Deserialize<List<T>>(File.ReadAllText(_path), JsonOptions) ?? [];
            _loadedWriteTime = writeTime;
        }
    }
}
