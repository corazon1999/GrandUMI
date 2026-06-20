using GrandUMI.Cards;

namespace GrandUMI.Game.Validation;

/// <summary>
/// 卡组合法性校验
///
/// 支持的格式（FormatRules）：
///   OP15-Only:   领航/主卡组仅限 OP15，50 张
///   OP16-Only:   领航/主卡组仅限 OP16，50 张
///   OP15-OP16:   领航/主卡组限 OP15 或 OP16，50 张（最新两弹联合）
/// </summary>
public static class DeckValidator
{
    public const string FormatUnrestricted = "Unrestricted";
    public const string FormatOp15Only = "OP15-Only";
    public const string FormatOp16Only = "OP16-Only";
    public const string FormatOp15Op16 = "OP15-OP16";

    /// <summary>格式规则：白名单卡集（null = 不限卡集） + 主卡组张数 + 同名上限</summary>
    private record FormatRule(string Name, string[]? AllowedSets, int MainSize, int CopyLimit);

    /// <summary>
    /// 不受「同名卡 ≤ CopyLimit」限制的卡（卡面注明"规则上，可以将任意张数的此卡牌放入卡组"）。
    /// 来源：卡牌原文检索"任意张数…放入卡组"。新增同类卡时在此登记。
    /// </summary>
    private static readonly HashSet<string> UnlimitedCopyCards = new()
    {
        "OP01-075", // 和平主义者
        "OP08-072", // 饼干士兵
        "OP16-042", // 因佩尔地狱的囚犯
    };

    private static readonly Dictionary<string, FormatRule> Rules = new()
    {
        [FormatUnrestricted] = new(FormatUnrestricted, null,             50, 4),
        [FormatOp15Only] = new(FormatOp15Only, new[] { "OP15" },         50, 4),
        [FormatOp16Only] = new(FormatOp16Only, new[] { "OP16" },         50, 4),
        [FormatOp15Op16] = new(FormatOp15Op16, new[] { "OP15", "OP16" }, 50, 4),
    };

    public record Result(bool Ok, string? Reason, string? LeaderNumber);

    /// <summary>
    /// 解析客户端 deck 字符串（DeckMapper 格式：
    ///   第 1 行 = 领航卡号，第 2~51 行 = 50 张主卡组）
    /// 并校验是否符合指定格式
    /// </summary>
    public static Result Validate(string deckRaw, string format = FormatUnrestricted)
    {
        if (string.IsNullOrWhiteSpace(deckRaw))
            return new(false, "卡组为空", null);

        if (!Rules.TryGetValue(format, out var rule))
            return new(false, $"未知卡组格式：{format}", null);

        var lines = deckRaw
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();

        if (lines.Length < 2)
            return new(false, "卡组格式错误：至少需要 1 张领航 + 1 张主卡组", null);

        var leaderNum = lines[0];
        var leader    = CardDatabase.Get(leaderNum);
        if (leader is null)
            return new(false, $"领航卡不存在：{leaderNum}", null);
        if (leader.Kind != CardKind.Leader)
            return new(false, $"指定的领航卡 {leaderNum} 不是领航类型", null);

        var mainCards = lines.Skip(1).ToArray();
        return ValidateAgainstRule(leader, mainCards, rule);
    }

    private static Result ValidateAgainstRule(CardInfo leader, string[] mainCards, FormatRule rule)
    {
        var allowed = rule.AllowedSets;
        var setList = allowed is null ? "" : string.Join("/", allowed);

        // 1. 领航必须来自白名单卡集（allowed=null 表示不限卡集，跳过此检查）
        if (allowed is not null && !allowed.Contains(leader.SetCode))
            return new(false, $"{rule.Name} 格式：领航必须来自 {setList}（当前 {leader.SetCode}）", leader.Number);

        // 2. 主卡组张数
        if (mainCards.Length != rule.MainSize)
            return new(false, $"{rule.Name} 格式：主卡组必须 {rule.MainSize} 张（当前 {mainCards.Length}）", leader.Number);

        // 3. 同名上限
        var counts = new Dictionary<string, int>();
        foreach (var n in mainCards)
            counts[n] = counts.GetValueOrDefault(n, 0) + 1;
        foreach (var (num, cnt) in counts)
            if (cnt > rule.CopyLimit && !UnlimitedCopyCards.Contains(num))
                return new(false, $"同名卡超过 {rule.CopyLimit} 张：{num} × {cnt}", leader.Number);

        // 4. 每张卡：存在 + 非领航 + 卡集白名单 + 颜色与领航相容
        foreach (var num in counts.Keys)
        {
            var card = CardDatabase.Get(num);
            if (card is null)
                return new(false, $"卡牌不存在：{num}", leader.Number);
            if (card.Kind == CardKind.Leader)
                return new(false, $"主卡组不能包含领航卡：{num}", leader.Number);
            if (allowed is not null && !allowed.Contains(card.SetCode))
                return new(false, $"{rule.Name} 格式：主卡组不能包含 {num}（{card.SetCode} 卡集）", leader.Number);
            if (!card.SharesColorWith(leader))
                return new(false, $"颜色不符：{num}（{card.Color}）与领航（{leader.Color}）无共同颜色", leader.Number);
        }

        return new(true, null, leader.Number);
    }
}
