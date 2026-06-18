using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-059 战略调整，高调大作战……（事件，水，因佩尔地狱/巴奇海盗团）
/// 【主要】可以将我方的 7 张咚!! 转为休息状态：确认我方卡组最上方的 5 张卡牌，
///         将其中最多 2 张力量不高于 6000 且拥有《因佩尔地狱》特征的角色卡牌登场。
///         之后，将剩余的卡牌自选顺序放回卡组最下方。
/// 【反击】本次战斗中，我方领袖力量+3000。
///
/// 实现说明 / 简化点：
///   - 【主要】可选成本 = 将我方 7 张活跃咚转为休息状态（不足 7 张则无法发动）。
///   - 看顶 5 张，从中选最多 2 张（力量≤6000 且具《因佩尔地狱》特征）的角色登场。
///   - 剩余卡牌按原相对顺序放回卡组最下方（自选顺序简化为原序）。
///   - 客户端通过 prompt 的 extra.choiceCards 显示卡组牌的卡面。
/// </summary>
public class OP16_059_StrategicAdjustment : IScriptedEffect
{
    public string CardNumber => "OP16-059";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // ── 【反击】 ──
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            AtomicOps.AddPowerThisBattle(me.Leader, 3000);
            return;
        }

        // ── 【主要】 ──
        // 成本：将我方 7 张活跃咚转为休息状态（不足 7 张则无法发动）
        var activeDons = me.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (activeDons.Count < 7) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "OP16-059【主要】：将我方 7 张咚!! 转为休息状态？（确认卡组顶 5 张，最多登场 2 张力量≤6000 的《因佩尔地狱》角色）");
        if (!use) return;

        // 支付成本：7 张活跃咚转休息
        for (int i = 0; i < 7; i++) activeDons[i].State = DonState.Rest;

        // 效果：确认卡组最上方 5 张
        int peek = Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        // 候选：力量≤6000 且具《因佩尔地狱》特征的角色
        var cand = top.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Power <= 6000 &&
            c.Info.HasKeyword("因佩尔地狱")).ToList();

        if (cand.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "登场最多 2 张力量≤6000 的《因佩尔地狱》角色",
                cand.Select(c => c.Id.ToString()).ToList(), 0, Math.Min(2, cand.Count), extra);

            foreach (var id in chosen)
            {
                var picked = cand.FirstOrDefault(c => c.Id.ToString() == id);
                if (picked is not null && me.Deck.Contains(picked))
                {
                    // 经手牌入口登场：先从卡组移入手牌，再走标准登场流程
                    me.Deck.Remove(picked);
                    me.Hand.Add(picked);
                    AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
                }
            }
        }

        // 之后：将剩余仍在顶部的卡牌按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
