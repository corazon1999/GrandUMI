using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-016 炎帝（事件 / 炎 7 费，白胡子海盗团）
/// 【主要】我方领袖为"波特夹斯·D·艾斯"的场合，将对方最多1张力量不高于8000的角色KO，
///   且本回合中，我方领袖获得【双重攻击】效果，力量+3000。
/// 【触发】将对方最多1张力量不高于6000的角色KO。
///
/// 实现说明：
///   - 【主要】(EventMain)：领袖名为"波特夹斯·D·艾斯"时，KO对方最多1张力量≤8000角色；
///     之后我方领袖本回合获得【双重攻击】(GiveKeyword ThisTurn) 且力量+3000(AddPowerThisTurn)。
///     （"此卡给予的伤害变为2"即【双重攻击】的固有效果，由关键词体现，无需额外处理。）
///   - 【触发】(OnLifeRevealTrigger)：KO对方最多1张力量≤6000角色。
///   - 当前力量用 CurrentPowerOf 评估。
/// </summary>
public class OP03_016_Enpou : IScriptedEffect
{
    public string CardNumber => "OP03-016";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            if (!me.Leader.Info.NameContains("波特夹斯·D·艾斯")) return;

            var cands = opp.Characters
                .Where(c => ctx.State.CurrentPowerOf(oppIdx, c) <= 8000).ToList();
            if (cands.Count > 0)
            {
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                    "将对方最多1张力量≤8000的角色KO",
                    cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
                if (chosen.Count > 0)
                {
                    var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                    AtomicOps.KO(ctx.State, oppIdx, tgt);
                }
            }

            // 本回合：我方领袖获得【双重攻击】、力量+3000
            AtomicOps.GiveKeyword(me.Leader, "双重攻击", KeywordDuration.ThisTurn);
            AtomicOps.AddPowerThisTurn(me.Leader, 3000);
        }
        else // OnLifeRevealTrigger
        {
            var cands = opp.Characters
                .Where(c => ctx.State.CurrentPowerOf(oppIdx, c) <= 6000).ToList();
            if (cands.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多1张力量≤6000的角色KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, oppIdx, tgt);
            }
        }
    }
}
