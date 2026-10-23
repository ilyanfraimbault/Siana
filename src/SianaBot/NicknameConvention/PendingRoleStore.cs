using Microsoft.Extensions.Options;
using SianaBot.Storage;

namespace SianaBot.NicknameConvention;

/// <summary>Un rôle réservé choisi par quelqu'un qui n'a pas (encore) le rôle de la convention de pseudo.</summary>
public sealed record PendingRole(ulong GuildId, ulong UserId, ulong RoleId);

public sealed class PendingRoleStore(IOptions<NicknameConventionOptions> options)
{
    private readonly JsonListStore<PendingRole> _store = new(options.Value.PendingStoragePath);

    public IReadOnlyList<ulong> For(ulong guildId, ulong userId) =>
        _store.All().Where(p => p.GuildId == guildId && p.UserId == userId).Select(p => p.RoleId).ToList();

    public Task AddAsync(ulong guildId, ulong userId, IReadOnlyCollection<ulong> roleIds) =>
        _store.MutateAsync(items =>
        {
            var added = false;
            foreach (var roleId in roleIds)
            {
                var pending = new PendingRole(guildId, userId, roleId);
                if (items.Contains(pending))
                    continue;
                items.Add(pending);
                added = true;
            }
            return added;
        });

    public Task<bool> RemoveAsync(ulong guildId, ulong userId, IReadOnlyCollection<ulong> roleIds) =>
        _store.MutateAsync(items =>
            items.RemoveAll(p => p.GuildId == guildId && p.UserId == userId && roleIds.Contains(p.RoleId)) > 0);
}
