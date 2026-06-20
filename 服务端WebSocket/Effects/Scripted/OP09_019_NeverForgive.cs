using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-019 我绝不轻易放过伤害我朋友的家伙!!!（事件）
/// 【主要】我方领袖拥有《红发海盗团》特征的场合，本回合中，对方最多 1 张角色力量 -3000。
///   之后，对方场上存在力量为 5000 或更高的角色的场合，抽取 1 张卡牌。
/// 【触发】抽取 1 张卡牌。（生命触发，由引擎处理触发节）
///
/// 说明 / 简化点：
/// - "对方场上存在力量≥5000的角色" 用 ctx.State.CurrentPowerOf 取当前力量判断存在性（在 -3000 结算之后评估）。
/// </summary>
public class OP09_019_NeverForgive : IScriptedEffect
{
    public string CardNumber => "OP09-019";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;

        // 【触发】抽 1
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        // 【主要】仅当领袖拥有《红发海盗团》时
        if (!me.Leader.Info.HasKeyword("红发海盗团")) return;

        // 对方最多 1 张角色 -3000
        var cands = opp.Characters.ToList();
        if (cands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方 1 张角色本回合 -3000（最多 1 张）",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddPowerThisTurn(tgt, -3000);
            }
        }

        // 之后：对方场上存在力量≥5000的角色则抽 1
        if (opp.Characters.Any(c => ctx.State.CurrentPowerOf(oppIdx, c) >= 5000))
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        }
    }
}
