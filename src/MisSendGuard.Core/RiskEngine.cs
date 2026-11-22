namespace MisSendGuard.Core;

public sealed record GuardOptions
{
    /// <summary>评估总时间预算。超时则按已完成的规则出结果,绝不阻塞发送。</summary>
    public TimeSpan TimeBudget { get; init; } = TimeSpan.FromMilliseconds(80);

    /// <summary>达到该分数给轻提醒。</summary>
    public int SoftWarnThreshold { get; init; } = 40;

    /// <summary>达到该分数给强提醒。</summary>
    public int HardWarnThreshold { get; init; } = 60;

    /// <summary>达到该分数延迟发送(可撤回窗口)。</summary>
    public int DelayThreshold { get; init; } = 85;

    /// <summary>
    /// 某规则的 override 次数超过 heed 次数且达到该值时,
    /// 该规则对此用户的分数按 SuppressedFactor 衰减——
    /// 用户已经用行为证明这条规则对他是噪音。
    /// </summary>
    public int OverrideSuppressAfter { get; init; } = 3;
    public double SuppressedFactor { get; init; } = 0.4;
}

/// <summary>
/// 风险评估引擎。设计约束:
/// 1. 快:所有规则同步在内存中完成,整体有硬时间预算,超预算降级放行已算出的部分。
/// 2. 准:风险相对于"这个用户自己的历史"判断,外部邮件本身不加分。
/// 3. 不烦:分数制 + 分级动作 + 基于用户反馈的自适应衰减,控制提醒总量。
/// </summary>
public sealed class RiskEngine
{
    private readonly IReadOnlyList<IRiskRule> _rules;
    private readonly IHistoryStore _history;
    private readonly GuardOptions _options;

    public RiskEngine(IHistoryStore history, GuardOptions? options = null, IEnumerable<IRiskRule>? rules = null)
    {
        _history = history;
        _options = options ?? new GuardOptions();
        _rules = rules?.ToList() ?? DefaultRules();
    }

    public static List<IRiskRule> DefaultRules() =>
    [
        new Rules.NewExternalWithSensitiveContentRule(),
        new Rules.SimilarAddressRule(),
        new Rules.DisplayNameMismatchRule(),
        new Rules.ReplyAllExternalLeakRule(),
        new Rules.MassExternalRule(),
    ];

    public Verdict Evaluate(OutgoingEmail email)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 纯内部邮件直接放行——这是最常见的正常场景,零打扰
        if (email.Recipients.All(r => _history.InternalDomains.Contains(r.Domain)))
            return Verdict.Clean with { Elapsed = sw.Elapsed };

        var ctx = EvalContext.Build(email, _history);
        var hits = new List<RuleHit>();

        foreach (var rule in _rules)
        {
            if (sw.Elapsed > _options.TimeBudget)
                break; // 超预算:用已有结果出结论,发送不被拖慢

            var hit = rule.Evaluate(ctx);
            if (hit is null) continue;

            hits.Add(ApplyAdaptiveSuppression(hit));
        }

        var total = hits.Sum(h => h.Score);
        var capped = Math.Min(total, 100);

        var action = capped switch
        {
            _ when capped >= _options.DelayThreshold => RiskAction.Delay,
            _ when capped >= _options.HardWarnThreshold => RiskAction.HardWarn,
            _ when capped >= _options.SoftWarnThreshold => RiskAction.SoftWarn,
            _ => RiskAction.Allow,
        };

        return new Verdict(action, capped, hits, sw.Elapsed);
    }

    /// <summary>
    /// 自适应降疲劳:用户反复 override 某条规则,说明该规则对他是误报,
    /// 衰减其分数。这样提醒总量会随使用自动收敛到"真正有用的提醒"。
    /// </summary>
    private RuleHit ApplyAdaptiveSuppression(RuleHit hit)
    {
        var (overrides, heeds) = _history.GetRuleFeedback(hit.RuleId);
        if (overrides >= _options.OverrideSuppressAfter && overrides > heeds * 2)
        {
            return hit with { Score = (int)(hit.Score * _options.SuppressedFactor) };
        }
        return hit;
    }
}
