namespace MisSendGuard.Core;

/// <summary>单个联系人的历史往来统计。</summary>
public sealed record ContactStats(
    string Address,
    string? DisplayName,
    /// <summary>过去 N 天内向该地址发送的邮件数。</summary>
    int SentCount,
    DateTimeOffset LastSentAt,
    /// <summary>是否为本公司域内地址。</summary>
    bool IsInternal);

/// <summary>
/// 历史往来与行为反馈的存储接口。生产环境用 SQLite/Exchange 增量同步实现;
/// 引擎只依赖这个接口,所有查询都应是 O(1) 字典查找,保证发送路径上的延迟。
/// </summary>
public interface IHistoryStore
{
    ContactStats? GetContact(string address);

    /// <summary>返回全部已知联系人(启动时加载进内存索引,供相似度匹配)。</summary>
    IReadOnlyCollection<ContactStats> GetAllContacts();

    IReadOnlySet<string> InternalDomains { get; }

    /// <summary>记录一次实际发送(用于更新历史)。</summary>
    void RecordSend(OutgoingEmail email, DateTimeOffset at);

    /// <summary>
    /// 记录用户在提醒后仍然点了"确认发送"(override)。
    /// 自适应降疲劳的核心:某条规则对某个用户反复被 override,说明它对这个用户是噪音。
    /// </summary>
    void RecordOverride(string ruleId, DateTimeOffset at);

    /// <summary>记录提醒被采纳(用户取消/修改了邮件)。</summary>
    void RecordHeeded(string ruleId, DateTimeOffset at);

    /// <summary>返回 (override 次数, heed 次数),供自适应阈值使用。</summary>
    (int Overrides, int Heeds) GetRuleFeedback(string ruleId);
}
