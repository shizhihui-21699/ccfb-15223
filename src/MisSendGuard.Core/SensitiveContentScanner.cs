using System.Text.RegularExpressions;

namespace MisSendGuard.Core;

public enum Sensitivity { None, Business, Personal }

public sealed record ContentScanResult(
    Sensitivity Level,
    /// <summary>命中的类别描述,如 "报价类关键词"、"身份证号"。</summary>
    IReadOnlyList<string> MatchedCategories);

/// <summary>
/// 敏感内容扫描器。全部正则预编译 + GeneratedRegex,单次扫描为微秒级;
/// 调用方负责限定扫描文本长度(截断长正文/大附件),保证发送路径延迟可控。
/// </summary>
public static partial class SensitiveContentScanner
{
    // 商务敏感:合同、报价、底价、折扣等
    [GeneratedRegex(@"合同|报价|报价单|底价|成本价|折扣|中标|投标|账期|框架协议|contract|quotation|pricing|discount", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BusinessTerms();

    // 个人敏感:身份证、银行卡、手机号、护照
    [GeneratedRegex(@"\b\d{17}[\dXx]\b|\b\d{15}\b")]
    private static partial Regex IdCard();

    [GeneratedRegex(@"\b\d{16,19}\b")]
    private static partial Regex BankCard();

    [GeneratedRegex(@"\b1[3-9]\d{9}\b")]
    private static partial Regex PhoneNumber();

    /// <summary>敏感文件名:合同/报价/工资/花名册等。</summary>
    [GeneratedRegex(@"合同|报价|工资|薪资|花名册|名单|身份证|银行卡|contract|quote|salary|payroll", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveFileName();

    /// <summary>单次扫描的最大字符数,超出截断。防止超大正文拖慢发送。</summary>
    public const int MaxScanChars = 64 * 1024;

    public static ContentScanResult Scan(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return new ContentScanResult(Sensitivity.None, []);

        if (text.Length > MaxScanChars)
            text = text[..MaxScanChars];

        var categories = new List<string>();
        var level = Sensitivity.None;

        if (BusinessTerms().IsMatch(text))
        {
            categories.Add("商务敏感词(合同/报价等)");
            level = Sensitivity.Business;
        }
        if (IdCard().IsMatch(text)) { categories.Add("身份证号"); level = Sensitivity.Personal; }
        if (BankCard().IsMatch(text)) { categories.Add("银行卡号"); level = Sensitivity.Personal; }
        if (PhoneNumber().IsMatch(text))
        {
            categories.Add("手机号");
            if (level < Sensitivity.Business) level = Sensitivity.Business;
        }

        return new ContentScanResult(level, categories);
    }

    public static bool IsSensitiveFileName(string fileName) => SensitiveFileName().IsMatch(fileName);
}
