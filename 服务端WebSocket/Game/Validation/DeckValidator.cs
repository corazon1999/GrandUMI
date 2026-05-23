using GrandUMI.Cards;

namespace GrandUMI.Game.Validation;

/// <summary>
/// 卡组合法性校验
/// 当前格式：OP15-Only（官方 50 张 + 仅限 OP15 卡池）
/// </summary>
public static class DeckValidator
{
    public const string FormatOp15Only = "OP15-Only";

    public record Result(bool Ok, string? Reason, string? LeaderNumber);

    /// <summary>
    /// 解析客户端 deck 字符串（DeckMapper 格式：
    ///   第 1 行 = 领航卡号，第 2~51 行 = 50 张主卡组）
    /// 并校验是否符合指定格式
    /// </summary>
    public static Result Validate(string deckRaw, string format = FormatOp15Only)
    {
        if (string.IsNullOrWhiteSpace(deckRaw))
            return new(false, "卡组为空", null);

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

        // 按格式分支
        return format switch
        {
            FormatOp15Only => ValidateOp15Only(leader, mainCards),
            _              => new(false, $"未知卡组格式：{format}", leaderNum),
        };
    }

    private static Result ValidateOp15Only(CardInfo leader, string[] mainCards)
    {
        // 1. 领航必须是 OP15
        if (leader.SetCode != "OP15")
            return new(false, $"OP15-Only 格式：领航必须来自 OP15（当前 {leader.SetCode}）", leader.Number);

        // 2. 主卡组必须 50 张
        if (mainCards.Length != 50)
            return new(false, $"OP15-Only 格式：主卡组必须 50 张（当前 {mainCards.Length}）", leader.Number);

        // 3. 同名卡 ≤ 4 张
        var counts = new Dictionary<string, int>();
        foreach (var n in mainCards)
            counts[n] = counts.GetValueOrDefault(n, 0) + 1;
        foreach (var (num, cnt) in counts)
            if (cnt > 4)
                return new(false, $"同名卡超过 4 张：{num} × {cnt}", leader.Number);

        // 4. 每张卡都必须来自 OP15 且颜色与领航相容
        foreach (var num in counts.Keys)
        {
            var card = CardDatabase.Get(num);
            if (card is null)
                return new(false, $"卡牌不存在：{num}", leader.Number);
            if (card.Kind == CardKind.Leader)
                return new(false, $"主卡组不能包含领航卡：{num}", leader.Number);
            if (card.SetCode != "OP15")
                return new(false, $"OP15-Only 格式：主卡组不能包含 {num}（{card.SetCode} 卡集）", leader.Number);
            if (!card.SharesColorWith(leader))
                return new(false, $"颜色不符：{num}（{card.Color}）与领航（{leader.Color}）无共同颜色", leader.Number);
        }

        return new(true, null, leader.Number);
    }
}
