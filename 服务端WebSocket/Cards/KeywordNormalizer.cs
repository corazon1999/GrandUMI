using System.Text;

namespace GrandUMI.Cards;

/// <summary>
/// 卡牌特征匹配标准化。仅用于规则结算，不修改卡牌数据和界面显示文字。
/// </summary>
public static class KeywordNormalizer
{
    private static readonly HashSet<char> DecorativeBrackets =
    [
        '〈', '〉', '《', '》', '【', '】', '「', '」', '『', '』',
    ];

    /// <summary>将常见排版及翻译差异转换为统一的规则匹配文本。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var compatible = value.Normalize(NormalizationForm.FormKC);
        var compact = new StringBuilder(compatible.Length);
        foreach (char ch in compatible)
        {
            if (char.IsWhiteSpace(ch) || DecorativeBrackets.Contains(ch)) continue;
            compact.Append(ch);
        }

        return compact.ToString()
            .Replace("海賊團", "海盗团", StringComparison.Ordinal)
            .Replace("海賊団", "海盗团", StringComparison.Ordinal)
            .Replace("海盜團", "海盗团", StringComparison.Ordinal)
            .Replace("海贼团", "海盗团", StringComparison.Ordinal);
    }

    public static bool Equals(string? left, string? right)
        => string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    public static bool Contains(string? source, string? fragment)
    {
        var normalizedFragment = Normalize(fragment);
        return normalizedFragment.Length > 0
               && Normalize(source).Contains(normalizedFragment, StringComparison.Ordinal);
    }
}
