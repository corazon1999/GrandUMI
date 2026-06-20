using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-008 克里克（角色 / 炎 8 费 9000）
/// 【登场时】赋予对方 1 张角色最多 3 张对方休息状态的咚!!。之后，本回合中，此角色获得【速攻】。
/// 【启动主要】【每回合1次】在此角色登场的回合的场合，本回合中，对方所有角色按其自身被赋予的
///   咚!!数量，每被赋予 1 张咚!! 该角色的力量 -1000。
///
/// 实现说明 / 简化点：
///   - 【登场时】赋予对方角色咚：从对方费用区的休息咚中取最多 3 张附给所选对方角色。
///   - "获得【速攻】"用 GiveKeyword(self, "速攻", ThisTurn)。
///   - 【启动主要】力量惩罚按发动时刻各对方角色的附咚数量作快照（本回合）应用
///     （AddPowerThisTurn 负值）。引擎 ContinuousEffect 的 PowerDelta 为定值，无法表达
///     "每张咚 -1000"的逐卡动态量，故采用发动时快照，对实战结果一致性高。
/// </summary>
public class OP15_008_Krieg : IScriptedEffect
{
    public string CardNumber => "OP15-008";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 赋予对方 1 张角色最多 3 张对方休息状态的咚!!
            if (opp.Characters.Count > 0)
            {
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                    "选择 1 张对方角色，赋予其最多 3 张对方休息状态的咚!!",
                    opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
                if (chosen.Count > 0)
                {
                    var tgt = opp.Characters.First(c => c.Id.ToString() == chosen[0]);
                    AtomicOps.AttachDonFromCost(opp, tgt.Id, 3, DonState.Rest);
                }
            }

            // 之后，本回合中此角色获得【速攻】
            AtomicOps.GiveKeyword(self, "速攻", KeywordDuration.ThisTurn);
            return;
        }

        // ── 【启动主要】【每回合1次】 ──
        if (ctx.Trigger == EffectTrigger.ActivatedMain)
        {
            // 仅在此角色登场的回合可发动
            if (self.TurnPlayed != ctx.State.TurnCount) return;

            var key = self.Info.Number + "-act" + ":" + self.Id;
            if (me.TurnOnceUsed.Contains(key)) return;

            // 本回合中：对方每个角色按其附咚数量 -1000/张
            bool any = false;
            foreach (var c in opp.Characters)
            {
                int dons = opp.AttachedDonCount(c.Id);
                if (dons > 0)
                {
                    AtomicOps.AddPowerThisTurn(c, -1000 * dons);
                    any = true;
                }
            }

            me.TurnOnceUsed.Add(key);
            _ = any;
        }
    }
}
