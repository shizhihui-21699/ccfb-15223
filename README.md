# MisSendGuard — 邮件误发拦截助手

在点击发送的瞬间评估收件人组合与邮件内容,对高风险邮件给出分级提醒或延迟发送。
核心库与客户端无关,可由 Outlook VSTO 加载项、Web 版 Office.js 加载项或邮件网关调用。

## 设计思路:怎么解决"提醒太多就被无视"

这是此类工具失败的头号原因,方案是四层防线:

1. **风险是相对的,不是绝对的。** 给外部发邮件本身不加分。风险 = 偏离该员工自己的
   历史行为:首次联系的外部地址、与常联系人高度相似的地址、回复全部带入的新面孔。
   老客户的正常报价邮件静默放行(见演示场景2)。
2. **纯内部邮件零评估直接放行。** 占比最高的场景零打扰、零耗时。
3. **分级动作,不是一律弹窗。** 低分静默 → 中分轻提醒(/可一键继续 → 高分强提醒
   (须勾选确认)→ 极高分延迟发送(可撤回窗口)。强提醒只留给真正危险的组合。
4. **自适应降疲劳。** 每条规则记录用户的"忽略(override)/采纳(heed)"反馈;
   某规则被同一用户连续忽略,其分数自动衰减(`GuardOptions.OverrideSuppressAfter`),
   提醒总量随使用收敛到"对这个用户真正有用的提醒"。

## 规则一览

| 规则 | 针对的事故 | 基础分 |
|---|---|---|
| `new-external-sensitive` | 敏感内容(合同/报价/身份证)发给陌生外部地址 | 45+ |
| `similar-address` | 自动补全选错:地址与常联系人 Jaro-Winkler 相似度 ≥0.93 | 55 |
| `display-name-mismatch` | 显示名是内部同事、地址却是陌生外部域 | 60 |
| `replyall-external-leak` | 回复全部带入会话外地址;多个客户域互相可见 | 35~80 |
| `mass-external` | 大量外部地址用 To/CC 群发(应改 BCC) | 40 |

分数可叠加,封顶 100。阈值见 `GuardOptions`(默认 40 轻提醒 / 60 强提醒 / 85 延迟)。

## 性能:发送不能变慢

- 所有检查在内存中同步完成,实测单次评估 **<1ms**(见演示输出)。
- 历史往来是 O(1) 字典查找;联系人在启动时加载进内存索引,后台增量更新。
- 敏感词用 `GeneratedRegex` 预编译;正文/附件文本扫描截断到 64KB。
- 引擎有硬时间预算(默认 80ms),超时按已完成规则出结果,**绝不阻塞发送**。
- 附件文本提取(Office 文档解析)由宿主在后台异步完成,不在发送路径上;
  发送时只消费已提取的文本。

## 接入方式(以 Outlook VSTO 为例)

```csharp
// Application.ItemSend 事件里(发送前同步调用,评估本身 <1ms):
var email = MapToOutgoingEmail(item);          // 映射收件人/附件/ReplyAll/会话参与者
var verdict = engine.Evaluate(email);
switch (verdict.Action)
{
    case RiskAction.HardWarn:
        cancel = !ShowConfirmDialog(verdict.Hits);   // 阻断式,须勾选确认
        if (cancel) history.RecordHeeded(topRule);   // 反馈回自适应系统
        else history.RecordOverride(topRule);
        break;
    case RiskAction.Delay:
        MoveToOutboxWithUndoWindow(item, seconds: 30);
        break;
}
```

`IHistoryStore` 需替换为持久化实现(SQLite + Exchange 增量同步),
`InMemoryHistoryStore` 仅供演示和测试。

## 运行

```bash
dotnet test                          # 9 个单元测试
dotnet run --project src/MisSendGuard.Demo   # 5 个场景演示 + 自适应降疲劳演示
```
