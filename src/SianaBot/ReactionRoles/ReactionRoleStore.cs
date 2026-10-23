using Microsoft.Extensions.Options;
using SianaBot.Storage;

namespace SianaBot.ReactionRoles;

public sealed class ReactionRoleStore(IOptions<ReactionRoleOptions> options)
{
    private readonly JsonListStore<ReactionRole> _store = new(options.Value.StoragePath);

    public IReadOnlyList<ReactionRole> All() => _store.All();

    public ReactionRole? Find(ulong messageId, string emoteKey) =>
        All().FirstOrDefault(r => r.MessageId == messageId && r.EmoteKey == emoteKey);

    public IReadOnlyList<ReactionRole> ForGuild(ulong guildId) =>
        All().Where(r => r.GuildId == guildId).ToList();

    public Task UpsertAsync(ReactionRole reactionRole) => UpsertManyAsync([reactionRole]);

    public Task UpsertManyAsync(IReadOnlyCollection<ReactionRole> reactionRoles) =>
        _store.MutateAsync(items =>
        {
            foreach (var reactionRole in reactionRoles)
            {
                items.RemoveAll(r => r.MessageId == reactionRole.MessageId && r.EmoteKey == reactionRole.EmoteKey);
                items.Add(reactionRole);
            }
            return true;
        });

    public Task<bool> RemoveAsync(ulong messageId, string emoteKey) =>
        _store.MutateAsync(items => items.RemoveAll(r => r.MessageId == messageId && r.EmoteKey == emoteKey) > 0);
}
