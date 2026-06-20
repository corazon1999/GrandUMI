using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-002 特拉法尔加·罗（领航）
/// 【启动主要】【每回合1次】②（可以将费用区中指定数量的咚!!转为休息状态）：
///   我方场上存在5张角色的场合，将我方1张角色放回持有者的手牌，
///   并将我方手牌中最多1张与放回角色颜色不同且费用不高于5的角色卡牌登场。
///
/// 实现说明：
///   - 成本为②（将2张活跃咚转为休息），用 ReturnDonToDeck 不合适——②是横置费用区咚。
///     用直接横置活跃咚 2 张实现（在 CostArea 中将 2 张活跃咚标记为休息）。
///   - "颜色不同"：捕获被退回角色的 ColorList，登场卡 filter 要求其颜色集合与之无交集。
///   - 每回合1次：TurnOnceUsed。
/// </summary>
public class OP01_002_Law : IScriptedEffect
{
    public string CardNumber => "OP01-002";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 条件：我方场上存在 5 张角色
        if (me.Characters.Count < 5) return;

        // 成本前置：需有 2 张活跃咚可横置
        int activeDon = me.CostArea.Count(d => d.State == DonState.Active);
        if (activeDon < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "特拉法尔加·罗【启动主要】：横置2张活跃咚!!，将我方1张角色退回手牌并登场1张颜色不同、费用≤5的角色？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);

        // 成本：横置费用区 2 张活跃咚
        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= 2) break;
            if (d.State == DonState.Active) { d.State = DonState.Rest; rested++; }
        }

        // 选择我方 1 张角色退回手牌
        var chars = me.Characters.ToList();
        if (chars.Count == 0) return;
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方1张角色退回手牌",
            chars.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count == 0) return;
        var bounced = chars.First(c => c.Id.ToString() == pick[0]);
        var bouncedColors = bounced.Info.ColorList;
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, bounced);

        // 登场：手牌中最多1张 颜色不同(与被退回角色颜色无交集) 且费用≤5 的角色
        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 5 &&
            !c.Info.ColorList.Any(col => bouncedColors.Contains(col))
        ).ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场1张颜色不同、费用≤5的角色（最多1张）",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
