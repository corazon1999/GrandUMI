using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-094 雷霆 破坏剑（事件 / 地 / 德莱斯罗兹）
/// 【主要】选择对方最多 1 张费用不高于 4 的角色 KO。
///   我方废弃区中有 15 张或更多卡牌的场合，改为选择对方费用不高于 6 的角色。
/// 【触发】可以将我方领袖转为休息状态：将对方最多 1 张费用不高于 5 的角色 KO。
///
/// 实现说明：
///   - 主要(EventMain)：KO 目标费用上限随我方废弃区张数 ≥15 在 4/6 间切换（脚本按当前张数算阈值）。
///   - 触发(OnLifeRevealTrigger)：可选成本=横置我方领袖；支付后 KO 对方费用≤5 角色。
/// </summary>
public class OP04_094_RaijinHakaiken : IScriptedEffect
{
    public string CardNumber => "OP04-094";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            int cap = me.Trash.Count >= 15 ? 6 : 4;
            var cands = opp.Characters.Where(c => c.Info.Cost <= cap).ToList();
            if (cands.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                $"将对方最多 1 张费用≤{cap} 的角色 KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
            return;
        }

        // 【触发】
        if (me.Leader.IsTapped) return; // 领袖须为活跃才可横置支付成本
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "雷霆 破坏剑【触发】：将我方领袖转为休息状态，KO 对方最多 1 张费用≤5 的角色？");
        if (!use) return;

        AtomicOps.RestCard(me.Leader);

        var t5 = opp.Characters.Where(c => c.Info.Cost <= 5).ToList();
        if (t5.Count == 0) return;
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤5 的角色 KO",
            t5.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var tgt = t5.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
