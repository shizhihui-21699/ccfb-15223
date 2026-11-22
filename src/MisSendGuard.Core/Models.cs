namespace MisSendGuard.Core;

/// <summary>邮件发送前的最终处置动作。</summary>
public enum RiskAction
{
    /// <summary>直接放行,用户无感知。</summary>
    Allow = 0,
    /// <summary>轻提醒:发送按钮旁的非阻断提示,可一键继续。用于中低风险。</summary>
    SoftWarn = 1,
    /// <summary>强提醒:阻断式弹窗,必须勾选"我确认收件人无误"才能发送。用于高风险。</summary>
    HardWarn = 2,
    /// <summary>延迟发送:进入"待发出"队列,N 秒内可撤回。用于极高风险。</summary>
    Delay = 3,
}

public sealed record Recipient(string Address, string? DisplayName, RecipientKind Kind)
{
    public string Domain => Address.Split('@') is [_, var d] ? d.ToLowerInvariant() : "";
    public string LocalPart => Address.Split('@')[0].ToLowerInvariant();
}

public enum RecipientKind { To, Cc, Bcc }

public sealed record AttachmentInfo(
    string FileName,
    long SizeBytes,
    /// <summary>附件正文提取出的纯文本(由宿主在后台预先提取,可能为 null 表示未提取/超时)。</summary>
    string? ExtractedText);

public sealed record OutgoingEmail(
    IReadOnlyList<Recipient> Recipients,
    string Subject,
    string Body,
    IReadOnlyList<AttachmentInfo> Attachments,
    /// <summary>是否是"回复全部"。</summary>
    bool IsReplyAll,
    /// <summary>原邮件会话(thread)中已出现过的参与者地址;非回复时为空。</summary>
    IReadOnlySet<string> ThreadParticipants,
    /// <summary>发件人自己的地址。</summary>
    string SenderAddress);

/// <summary>单条规则命中的结果。</summary>
public sealed record RuleHit(
    string RuleId,
    /// <summary>0-100 的风险贡献分。</summary>
    int Score,
    /// <summary>给用户看的一句话原因(必须具体,点名是哪个收件人/附件)。</summary>
    string Reason,
    /// <summary>命中的具体对象(地址/附件名),用于 UI 高亮。</summary>
    IReadOnlyList<string> Involved);

public sealed record Verdict(
    RiskAction Action,
    int TotalScore,
    IReadOnlyList<RuleHit> Hits,
    TimeSpan Elapsed)
{
    public static readonly Verdict Clean = new(RiskAction.Allow, 0, [], TimeSpan.Zero);
}
