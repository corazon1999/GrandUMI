using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-039 橡皮橡皮二档JET枪（事件，风，因佩尔地狱/草帽一伙）
/// 【主要】本回合中，我方最多 1 张"蒙奇·D·路飞"获得【双重攻击】效果。
///         之后，我方领袖拥有《因佩尔地狱》特征的场合，将对方最多 2 张费用不高于 3 的角色转为休息状态。
/// 【触发】将对方领袖转为休息状态。
///
/// 实现说明：
///   - 【主要】第一段：从我方卡名"蒙奇·D·路飞"的角色中选最多 1 张，本回合赋予【双重攻击】。
///   - 第二段：仅当我方领袖具《因佩尔地狱》特征时执行，将对方最多 2 张费用≤3 的角色转为休息状态。
///   - 【触发】(OnLifeRevealTrigger)：将对方领袖转为休息状态。
/// </summary>
public class OP16_039_GomuGomuNoJetGun : IScriptedEffect
{
    public string CardNumber => "OP16-039";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // ── 【触发】：将对方领袖转为休息状态 ──
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            AtomicOps.RestCard(opp.Leader);
            return;
        }

        // ── 【主要】第一段：我方最多 1 张"蒙奇·D·路飞"本回合获得【双重攻击】 ──
        var luffys = me.Characters.Where(c => c.MatchesName("蒙奇·D·路飞")).ToList();
        if (luffys.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLuffy",
                "选择我方最多 1 张\"蒙奇·D·路飞\"，本回合获得【双重攻击】",
                luffys.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var target = luffys.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.GiveKeyword(target, "双重攻击", KeywordDuration.ThisTurn);
            }
        }

        // ── 第二段：我方领袖具《因佩尔地狱》特征时，将对方最多 2 张费用≤3 的角色转为休息状态 ──
        if (!me.Leader.Info.HasKeyword("因佩尔地狱")) return;

        var candidates = opp.Characters.Where(c => c.Info.Cost <= 3).ToList();
        if (candidates.Count == 0) return;

        var rested = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacterCostLe3",
            "选择对方最多 2 张费用不高于 3 的角色，转为休息状态",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 2);
        foreach (var cid in rested)
        {
            var card = candidates.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null) AtomicOps.RestCard(card);
        }
    }
}
