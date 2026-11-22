namespace MisSendGuard.Core.Rules;

/// <summary>
/// 首次联系的外部收件人 + 敏感内容:误发事故的最典型组合。
/// 历史往来是白名单的基础——常联系的外部客户不触发。
/// </summary>
public sealed class NewExternalWithSensitiveContentRule : IRiskRule
{
    public string RuleId => "new-external-sensitive";

    /// <summary>往来达到该次数即视为"熟人"。</summary>
    public int FamiliarThreshold { get; init; } = 3;

    public RuleHit? Evaluate(EvalContext ctx)
    {
        if (!ctx.HasSensitiveContent || ctx.ExternalRecipients.Count == 0)
            return null;

        var unfamiliar = ctx.ExternalRecipients
            .Where(x => x.Stats is null || x.Stats.SentCount < FamiliarThreshold)
            .Select(x => x.R.Address)
            .ToList();

        if (unfamiliar.Count == 0)
            return null;

        var contentDesc = ctx.ContentScan.MatchedCategories.Count > 0
            ? string.Join("、", ctx.ContentScan.MatchedCategories)
            : $"敏感附件({string.Join("、", ctx.SensitiveAttachments)})";

        return new RuleHit(
            RuleId,
            Score: 45 + 10 * Math.Min(unfamiliar.Count - 1, 3),
            Reason: $"邮件含{contentDesc},但 {string.Join("、", unfamiliar)} 是很少联系的外部地址",
            Involved: unfamiliar);
    }
}

/// <summary>
/// 相似地址误选:收件人与通讯录中某个常联系地址高度相似但不同——
/// 自动补全选错同名联系人的典型特征(如 zhangwei@abc.com vs zhangwei@abd.com)。
/// </summary>
public sealed class SimilarAddressRule : IRiskRule
{
    public string RuleId => "similar-address";

    /// <summary>Jaro-Winkler 相似度阈值。0.93 以上基本只有 1-2 个字符差异。</summary>
    public double Threshold { get; init; } = 0.93;

    public RuleHit? Evaluate(EvalContext ctx)
    {
        var contacts = ctx.History.GetAllContacts();
        var hits = new List<string>();

        foreach (var (r, stats) in ctx.ExternalRecipients)
        {
            // 这个地址本身就是熟人的不算
            if (stats is { SentCount: >= 3 }) continue;

            foreach (var c in contacts)
            {
                if (c.Address.Equals(r.Address, StringComparison.OrdinalIgnoreCase)) continue;
                if (c.SentCount < 3) continue; // 只和"常联系人"比,避免噪音

                var sim = TextSimilarity.JaroWinkler(
                    r.Address.ToLowerInvariant(), c.Address.ToLowerInvariant());
                if (sim >= Threshold)
                {
                    hits.Add($"{r.Address}(与常联系的 {c.Address} 相似度 {sim:P0})");
                    break;
                }
            }
        }

        return hits.Count == 0
            ? null
            : new RuleHit(RuleId, 55,
                $"收件人 {string.Join("、", hits)},是否选错了自动补全?",
                hits);
    }
}

/// <summary>
/// 显示名与地址不符:显示名是公司同事,地址却是外部陌生域——
/// 同名外部联系人冒充/误选的信号。
/// </summary>
public sealed class DisplayNameMismatchRule : IRiskRule
{
    public string RuleId => "display-name-mismatch";

    public RuleHit? Evaluate(EvalContext ctx)
    {
        var contacts = ctx.History.GetAllContacts();
        var hits = new List<string>();

        foreach (var (r, stats) in ctx.ExternalRecipients)
        {
            if (r.DisplayName is not { Length: >= 2 } name) continue;
            if (stats is { SentCount: >= 1 }) continue; // 以前发过,说明用户知道这个地址

            // 显示名命中了某个内部同事的名字,但地址是陌生外部域
            var internalNamesake = contacts.FirstOrDefault(c =>
                c.IsInternal
                && c.DisplayName is { Length: >= 2 } dn
                && dn.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (internalNamesake is not null)
            {
                hits.Add($"{name} <{r.Address}>(公司内部同事 {internalNamesake.Address} 同名)");
            }
        }

        return hits.Count == 0
            ? null
            : new RuleHit(RuleId, 60,
                $"收件人 {string.Join("、", hits)} 与内部同事同名但使用外部地址,请确认是否选错人",
                hits);
    }
}

/// <summary>
/// 回复全部带入新外部人:原会话没有的外部地址被带进了回复全部,
/// 或多个互不相关的客户域同时出现在收件人里(甲客户的报价乙客户也能看到)。
/// </summary>
public sealed class ReplyAllExternalLeakRule : IRiskRule
{
    public string RuleId => "replyall-external-leak";

    public RuleHit? Evaluate(EvalContext ctx)
    {
        if (!ctx.Email.IsReplyAll || ctx.ExternalRecipients.Count == 0)
            return null;

        // 1) 回复全部引入了原会话中不存在的外部地址
        var newcomers = ctx.ExternalRecipients
            .Where(x => !ctx.Email.ThreadParticipants.Contains(x.R.Address))
            .Select(x => x.R.Address)
            .ToList();

        // 2) 收件人里出现 2 个以上互不相同的外部域(跨客户泄露)
        var externalDomains = ctx.ExternalRecipients
            .Select(x => x.R.Domain)
            .Distinct()
            .ToList();

        if (newcomers.Count == 0 && externalDomains.Count < 2)
            return null;

        var score = 0;
        var reasons = new List<string>();
        var involved = new List<string>();

        if (newcomers.Count > 0)
        {
            score += 35;
            reasons.Add($"回复全部带入了原会话之外的外部地址 {string.Join("、", newcomers)}");
            involved.AddRange(newcomers);
        }
        if (externalDomains.Count >= 2)
        {
            score += 30 + 5 * Math.Min(externalDomains.Count - 2, 4);
            reasons.Add($"收件人同时包含 {externalDomains.Count} 家不同外部单位({string.Join("、", externalDomains)}),彼此可见");
            involved.AddRange(externalDomains);
        }
        if (ctx.HasSensitiveContent) score += 15;

        return new RuleHit(RuleId, Math.Min(score, 80),
            string.Join(";并且", reasons), involved);
    }
}

/// <summary>
/// 群发外部:同一封邮件发给大量外部地址(非回复场景),
/// 通常是误操作或该用 BCC/邮件列表的场景。
/// </summary>
public sealed class MassExternalRule : IRiskRule
{
    public string RuleId => "mass-external";
    public int Threshold { get; init; } = 10;

    public RuleHit? Evaluate(EvalContext ctx)
    {
        var externalToCc = ctx.ExternalRecipients
            .Where(x => x.R.Kind != RecipientKind.Bcc)
            .Select(x => x.R.Address)
            .ToList();

        if (externalToCc.Count < Threshold)
            return null;

        return new RuleHit(RuleId, 40,
            $"邮件将同时发给 {externalToCc.Count} 个外部地址且彼此可见,是否需要改用密送(BCC)?",
            externalToCc.Take(5).ToList());
    }
}
