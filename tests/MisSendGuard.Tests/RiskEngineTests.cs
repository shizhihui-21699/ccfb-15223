using MisSendGuard.Core;

namespace MisSendGuard.Tests;

public class RiskEngineTests
{
    private static InMemoryHistoryStore NewHistory()
    {
        var h = new InMemoryHistoryStore(["ourcompany.com"]);
        var now = DateTimeOffset.Now;
        h.AddContact(new ContactStats("zhangwei@ourcompany.com", "张伟", 0, now, true));
        h.AddContact(new ContactStats("zhangwei@abc-trade.com", "张伟-ABC", 47, now, false));
        return h;
    }

    private static OutgoingEmail Email(
        Recipient[] recipients, string body = "", AttachmentInfo[]? attachments = null,
        bool replyAll = false, IReadOnlySet<string>? thread = null) =>
        new(recipients, "主题", body, attachments ?? [], replyAll,
            thread ?? new HashSet<string>(), "me@ourcompany.com");

    [Fact]
    public void InternalOnly_IsSilent()
    {
        var engine = new RiskEngine(NewHistory());
        var v = engine.Evaluate(Email([new Recipient("zhangwei@ourcompany.com", "张伟", RecipientKind.To)]));
        Assert.Equal(RiskAction.Allow, v.Action);
        Assert.Empty(v.Hits);
    }

    [Fact]
    public void FamiliarExternalWithQuote_IsAllowed()
    {
        var engine = new RiskEngine(NewHistory());
        var v = engine.Evaluate(Email(
            [new Recipient("zhangwei@abc-trade.com", "张伟-ABC", RecipientKind.To)],
            attachments: [new AttachmentInfo("报价单.xlsx", 1000, "报价 折扣")]));
        Assert.Equal(RiskAction.Allow, v.Action);
    }

    [Fact]
    public void SimilarAddress_AutocompleteMistake_HardWarns()
    {
        var engine = new RiskEngine(NewHistory());
        var v = engine.Evaluate(Email(
            [new Recipient("zhangwei@abd-trade.com", "张伟", RecipientKind.To)],
            attachments: [new AttachmentInfo("报价单.xlsx", 1000, "报价")]));
        Assert.Contains(v.Hits, h => h.RuleId == "similar-address");
        Assert.True(v.Action >= RiskAction.HardWarn);
    }

    [Fact]
    public void ReplyAll_NewExternalDomain_Triggers()
    {
        var engine = new RiskEngine(NewHistory());
        var v = engine.Evaluate(Email(
            [
                new Recipient("zhangwei@abc-trade.com", "张伟-ABC", RecipientKind.To),
                new Recipient("boss@other-client.com", null, RecipientKind.Cc),
            ],
            replyAll: true,
            thread: new HashSet<string> { "zhangwei@abc-trade.com", "me@ourcompany.com" }));
        Assert.Contains(v.Hits, h => h.RuleId == "replyall-external-leak");
    }

    [Fact]
    public void NewExternal_WithSensitiveAttachment_Warns()
    {
        var engine = new RiskEngine(NewHistory());
        var v = engine.Evaluate(Email(
            [new Recipient("x@unknown.com", null, RecipientKind.To)],
            attachments: [new AttachmentInfo("合同.docx", 1000, "合同 报价")]));
        Assert.Contains(v.Hits, h => h.RuleId == "new-external-sensitive");
        Assert.True(v.Action >= RiskAction.SoftWarn);
    }

    [Fact]
    public void NewExternal_PlainContent_NoWarn()
    {
        var engine = new RiskEngine(NewHistory());
        var v = engine.Evaluate(Email(
            [new Recipient("x@unknown.com", null, RecipientKind.To)],
            body: "您好,想咨询一下合作"));
        Assert.Equal(RiskAction.Allow, v.Action);
    }

    [Fact]
    public void DisplayName_ImpersonatesInternal_Triggers()
    {
        var engine = new RiskEngine(NewHistory());
        var v = engine.Evaluate(Email(
            [new Recipient("zhang.wei@evil-external.com", "张伟", RecipientKind.To)]));
        Assert.Contains(v.Hits, h => h.RuleId == "display-name-mismatch");
    }

    [Fact]
    public void AdaptiveSuppression_ReducesScoreAfterOverrides()
    {
        var history = NewHistory();
        var engine = new RiskEngine(history);
        var email = Email(
            [new Recipient("x@unknown.com", null, RecipientKind.To)],
            attachments: [new AttachmentInfo("合同.docx", 1000, "合同")]);

        var before = engine.Evaluate(email).TotalScore;
        for (var i = 0; i < 3; i++)
            history.RecordOverride("new-external-sensitive", DateTimeOffset.Now);
        var after = engine.Evaluate(email).TotalScore;

        Assert.True(after < before, $"expected {after} < {before}");
    }

    [Fact]
    public void Evaluation_IsFast()
    {
        var engine = new RiskEngine(NewHistory());
        var email = Email(
            [new Recipient("zhangwei@abd-trade.com", "张伟", RecipientKind.To)],
            body: new string('x', 200_000), // 超长正文
            attachments: [new AttachmentInfo("报价单.xlsx", 1000, "报价")]);
        var v = engine.Evaluate(email);
        Assert.True(v.Elapsed.TotalMilliseconds < 100, $"took {v.Elapsed.TotalMilliseconds}ms");
    }
}
