namespace MisSendGuard.Core;

/// <summary>
/// 字符串相似度,用于"同名/相似地址"检测。
/// Jaro-Winkler 对前缀一致(人名拼写差异通常在后半段)给更高分,适合邮箱地址。
/// </summary>
public static class TextSimilarity
{
    public static double JaroWinkler(string a, string b)
    {
        if (a == b) return 1.0;
        if (a.Length == 0 || b.Length == 0) return 0.0;

        var jaro = Jaro(a, b);
        // 公共前缀(最多 4 个字符)加权
        var prefix = 0;
        for (var i = 0; i < Math.Min(Math.Min(a.Length, b.Length), 4); i++)
        {
            if (a[i] != b[i]) break;
            prefix++;
        }
        return jaro + prefix * 0.1 * (1 - jaro);
    }

    private static double Jaro(string a, string b)
    {
        var matchDistance = Math.Max(a.Length, b.Length) / 2 - 1;
        if (matchDistance < 0) matchDistance = 0;

        var aMatched = new bool[a.Length];
        var bMatched = new bool[b.Length];
        var matches = 0;

        for (var i = 0; i < a.Length; i++)
        {
            var start = Math.Max(0, i - matchDistance);
            var end = Math.Min(i + matchDistance + 1, b.Length);
            for (var j = start; j < end; j++)
            {
                if (bMatched[j] || a[i] != b[j]) continue;
                aMatched[i] = true;
                bMatched[j] = true;
                matches++;
                break;
            }
        }
        if (matches == 0) return 0.0;

        var transpositions = 0;
        var k = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (!aMatched[i]) continue;
            while (!bMatched[k]) k++;
            if (a[i] != b[k]) transpositions++;
            k++;
        }

        return (matches / (double)a.Length
              + matches / (double)b.Length
              + (matches - transpositions / 2.0) / matches) / 3.0;
    }

    /// <summary>去掉数字后缀、分隔符,提取地址中的"人名部分"用于同名比较。</summary>
    public static string NormalizeName(string localPart)
    {
        var chars = localPart.ToLowerInvariant()
            .Where(c => char.IsLetter(c))
            .ToArray();
        return new string(chars);
    }
}
