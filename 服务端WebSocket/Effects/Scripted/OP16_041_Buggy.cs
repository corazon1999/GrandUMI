using System.Linq;
using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-041 巴奇（领航）
/// 【咚!!×1】【每回合1次】当我方拥有《因佩尔地狱》特征的角色离开场上时，可以将我方手牌中最多1张
///   "因佩尔地狱的囚犯"(OP16-042)登场。
///
/// 实现（旧脚本占位 bug：HandlesTrigger=OnKO 与卡数据 effectTags=OnCharLeaveField 不一致，永不触发；且缺咚门槛）：
///   - "离开场上"含战斗/效果KO（走 OnAnyCharKOd，被KO卡此刻在废弃区）与非KO离场（退手牌/回卡组/置生命，走 OnCharLeaveField）。
///     故同时监听这两个触发，覆盖全部离场路径。effectTags 已补 OnAnyCharKOd。
///   - 离场方须为我方、离场卡含《因佩尔地狱》特征；【咚×1】= 本领航被赋予咚≥1；【每回合1次】。
/// </summary>
public class OP16_041_Buggy : IScriptedEffect
{
    public string CardNumber => "OP16-041";
    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnAnyCharKOd || t == EffectTrigger.OnCharLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 离场方须为我方
        int leaveOwner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (leaveOwner != ctx.OwnerIndex) return;

        // 离场卡须非自身且含《因佩尔地狱》（此刻在废弃/手牌/卡组/生命，读 Info 静态数据）
        var cardId = ctx.Vars.TryGetValue("cardId", out var cv) ? cv as string : null;
        if (cardId is null || cardId == ctx.Source.Id.ToString()) return;
        var left = FindLeft(me, cardId);
        if (left is null || !left.Info.HasKeyword("因佩尔地狱")) return;

        // 【咚!!×1】
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return;

        // 【每回合1次】
        var key = "OP16-041-OncePerTurn:" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 手牌中"因佩尔地狱的囚犯"(OP16-042)
        var prisoners = me.Hand.Where(c => c.Info.Kind == CardKind.Character
            && (c.Info.Number == "OP16-042" || c.MatchesName("因佩尔地狱的囚犯"))).ToList();
        if (prisoners.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = prisoners.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandPrisoner",
            "可以将手牌中最多1张\"因佩尔地狱的囚犯\"(OP16-042)登场",
            prisoners.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        me.TurnOnceUsed.Add(key);
        var card = prisoners.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, card);
    }

    // 离场卡此刻可能在废弃/手牌/卡组/生命区，按常见顺序查找。
    // 还须搜场上角色区/舞台：同一效果链里离场的卡可能被后续操作再次登场回场上
    //（如 OP16-045 把费用≥2角色回手作成本、其收益又把这张≤2《因佩尔地狱》角色登场回场上）。
    // OnCharLeaveField 事件已证明它确实离场过，故全区域定位以读取特征是安全的。
    private static CardInstance? FindLeft(PlayerState me, string cardId)
        => me.Trash.FirstOrDefault(c => c.Id.ToString() == cardId)
        ?? me.Hand.FirstOrDefault(c => c.Id.ToString() == cardId)
        ?? me.Deck.FirstOrDefault(c => c.Id.ToString() == cardId)
        ?? me.LifeArea.FirstOrDefault(c => c.Id.ToString() == cardId)
        ?? me.Characters.FirstOrDefault(c => c.Id.ToString() == cardId)
        ?? (me.StageCard is { } st && st.Id.ToString() == cardId ? st : null);
}
