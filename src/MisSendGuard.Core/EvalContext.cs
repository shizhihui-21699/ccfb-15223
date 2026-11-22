namespace MisSendGuard.Core;

/// <summary>
/// 评估上下文:在规则运行前一次性算好所有共享特征,
/// 规则本身只做查表和比较,保证整体评估在毫秒级完成。
/// </summary>
public sealed class EvalContext
{
    public required OutgoingEmail Email { get; init; }
    public required IHistoryStore History { get; init; }

    /// <summary>外部收件人及其历史(无历史 = 首次联系,为 null)。</summary>
    public required IReadOnlyList<(Recipient R, ContactStats? Stats)> ExternalRecipients { get; init; }

    /// <summary>正文+附件文本合并扫描的结果。</summary>
    public required ContentScanResult ContentScan { get; init; }

    /// <summary>命中敏感文件名的附件。</summary>
    public required IReadOnlyList<string> SensitiveAttachments { get; init; }

    public bool HasSensitiveContent =>
        ContentScan.Level != Sensitivity.None || SensitiveAttachments.Count > 0;

    public static EvalContext Build(OutgoingEmail email, IHistoryStore history)
    {
        var externals = email.Recipients
            .Where(r => !history.InternalDomains.Contains(r.Domain))
            .Select(r => (r, history.GetContact(r.Address)))
            .ToList();

        // 正文 + 各附件提取文本合并扫描(附件文本由宿主后台预提取)
        var combinedText = email.Body;
        foreach (var a in email.Attachments)
        {
            if (a.ExtractedText is { Length: > 0 } t)
                combinedText += "\n" + t;
        }

        return new EvalContext
        {
            Email = email,
            History = history,
            ExternalRecipients = externals,
            ContentScan = SensitiveContentScanner.Scan(combinedText),
            SensitiveAttachments = email.Attachments
                .Where(a => SensitiveContentScanner.IsSensitiveFileName(a.FileName))
                .Select(a => a.FileName)
                .ToList(),
        };
    }
}

public interface IRiskRule
{
    string RuleId { get; }
    RuleHit? Evaluate(EvalContext ctx);
}
