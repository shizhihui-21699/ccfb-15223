using System.Collections.Concurrent;

namespace MisSendGuard.Core;

/// <summary>进程内实现,用于演示与测试;生产可替换为 SQLite 持久化版本。</summary>
public sealed class InMemoryHistoryStore : IHistoryStore
{
    private readonly ConcurrentDictionary<string, ContactStats> _contacts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (int Overrides, int Heeds)> _feedback = new();
    private readonly HashSet<string> _internalDomains;

    public InMemoryHistoryStore(IEnumerable<string> internalDomains)
    {
        _internalDomains = new HashSet<string>(internalDomains, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlySet<string> InternalDomains => _internalDomains;

    public ContactStats? GetContact(string address) =>
        _contacts.TryGetValue(address, out var s) ? s : null;

    public IReadOnlyCollection<ContactStats> GetAllContacts() => _contacts.Values.ToArray();

    public void AddContact(ContactStats stats) => _contacts[stats.Address] = stats;

    public void RecordSend(OutgoingEmail email, DateTimeOffset at)
    {
        foreach (var r in email.Recipients)
        {
            _contacts.AddOrUpdate(
                r.Address,
                _ => new ContactStats(r.Address, r.DisplayName, 1, at, _internalDomains.Contains(r.Domain)),
                (_, old) => old with
                {
                    SentCount = old.SentCount + 1,
                    LastSentAt = at,
                    DisplayName = r.DisplayName ?? old.DisplayName,
                });
        }
    }

    public void RecordOverride(string ruleId, DateTimeOffset at) =>
        _feedback.AddOrUpdate(ruleId, (1, 0), (_, f) => (f.Overrides + 1, f.Heeds));

    public void RecordHeeded(string ruleId, DateTimeOffset at) =>
        _feedback.AddOrUpdate(ruleId, (0, 1), (_, f) => (f.Overrides, f.Heeds + 1));

    public (int Overrides, int Heeds) GetRuleFeedback(string ruleId) =>
        _feedback.TryGetValue(ruleId, out var f) ? f : (0, 0);
}
