using MisSendGuard.Core;

// 模拟一家公司的真实使用场景:种子历史 + 四种发送情形
var history = new InMemoryHistoryStore(["ourcompany.com"]);
var now = DateTimeOffset.Now;

// 种子历史:张伟(内部同事)、老客户 zhangwei@abc-trade.com(常联系)、几个普通客户
history.AddContact(new ContactStats("zhangwei@ourcompany.com", "张伟", 0, now, IsInternal: true));
history.AddContact(new ContactStats("zhangwei@abc-trade.com", "张伟-ABC贸易", 47, now, IsInternal: false));
history.AddContact(new ContactStats("li.na@client-a.com", "李娜", 25, now, IsInternal: false));
history.AddContact(new ContactStats("wang.fang@client-b.com", "王芳", 18, now, IsInternal: false));

var engine = new RiskEngine(history);

var scenarios = new (string Title, OutgoingEmail Email)[]
{
    ("场景1:内部周报(最常见,应静默放行)",
        new OutgoingEmail(
            [new Recipient("zhangwei@ourcompany.com", "张伟", RecipientKind.To)],
            "本周周报", "详见附件", [], false,
            new HashSet<string>(), "me@ourcompany.com")),

    ("场景2:给老客户发更新后的报价(正常业务,应放行)",
        new OutgoingEmail(
            [new Recipient("zhangwei@abc-trade.com", "张伟-ABC贸易", RecipientKind.To)],
            "报价更新", "附件为最新报价单,请查收",
            [new AttachmentInfo("报价单-v3.xlsx", 80_000, "产品报价 折扣 账期")],
            false, new HashSet<string>(), "me@ourcompany.com")),

    ("场景3:自动补全选错——报价发给了相似地址 abd-trade(应强提醒)",
        new OutgoingEmail(
            [new Recipient("zhangwei@abd-trade.com", "张伟", RecipientKind.To)],
            "报价更新", "附件为最新报价单,请查收",
            [new AttachmentInfo("报价单-v3.xlsx", 80_000, "产品报价 折扣 账期")],
            false, new HashSet<string>(), "me@ourcompany.com")),

    ("场景4:回复全部带入了新客户域 + 附件含合同(应强提醒/延迟)",
        new OutgoingEmail(
            [
                new Recipient("li.na@client-a.com", "李娜", RecipientKind.To),
                new Recipient("boss@client-c.com", "某总", RecipientKind.Cc),
            ],
            "RE: 合同条款确认", "合同已更新,见附件",
            [new AttachmentInfo("合同-final.docx", 120_000, "合同 框架协议 账期")],
            IsReplyAll: true,
            ThreadParticipants: new HashSet<string> { "li.na@client-a.com", "me@ourcompany.com" },
            SenderAddress: "me@ourcompany.com")),

    ("场景5:给陌生外部地址发合同(首次联系+敏感附件,应提醒)",
        new OutgoingEmail(
            [new Recipient("someone@unknown-vendor.com", null, RecipientKind.To)],
            "合作合同", "请查收合同",
            [new AttachmentInfo("合同.docx", 100_000, "合同 报价")],
            false, new HashSet<string>(), "me@ourcompany.com")),
};

foreach (var (title, email) in scenarios)
{
    var verdict = engine.Evaluate(email);
    Console.WriteLine($"\n=== {title} ===");
    Console.WriteLine($"  动作: {verdict.Action}  总分: {verdict.TotalScore}  耗时: {verdict.Elapsed.TotalMilliseconds:F1}ms");
    foreach (var h in verdict.Hits)
        Console.WriteLine($"  - [{h.RuleId} +{h.Score}分] {h.Reason}");

    if (verdict.Action is RiskAction.Allow)
        history.RecordSend(email, now);
}

// 演示自适应降疲劳:同一规则被 override 3 次后分数衰减
Console.WriteLine("\n=== 自适应降疲劳演示 ===");
var riskyEmail = scenarios[4].Email;
Console.WriteLine($"首次评估: {engine.Evaluate(riskyEmail).TotalScore} 分");
for (var i = 0; i < 3; i++)
    history.RecordOverride("new-external-sensitive", now);
Console.WriteLine($"用户连续 3 次忽略该提醒后: {engine.Evaluate(riskyEmail).TotalScore} 分(分数已衰减)");
