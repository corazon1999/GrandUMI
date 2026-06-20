using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-021 佩罗娜（领航）
/// 【启动主要】【每回合1次】选择以下的 1 项：
///   ・将对方最多 1 张费用不高于 4 的角色转为休息状态。
///   ・本回合中，对方最多 1 张角色费用 -1。
///
/// 实现说明 / 简化点：
///   - "选择以下 1 项" 用 ChooseOption 返回下标后分支；wfReason 认为多选项无机制，但引擎提供 ChooseOption。
///   - "对方角色费用 -1" 用 AddCostModifier(本回合) 直接作用于对方角色，AtomicOps 不限定作用方。
///   - 每回合 1 次用 TurnOnceUsed 去重。
/// </summary>
public class OP06_021_Perona : IScriptedEffect
{
    public string CardNumber => "OP06-021";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-act";
        if (me.TurnOnceUsed.Contains(key)) return;

        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "佩罗娜：选择以下 1 项",
            new[] { "将对方最多 1 张费用≤4 的角色转为休息状态", "本回合中对方最多 1 张角色费用 -1" });

        if (opt == 0)
        {
            var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 4).ToList();
            if (cands.Count > 0)
            {
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                    "选择最多 1 张费用≤4 的对方角色转为休息状态",
                    cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
                if (chosen.Count > 0)
                {
                    var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                    AtomicOps.RestCard(tgt);
                }
            }
        }
        else
        {
            if (opp.Characters.Count > 0)
            {
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                    "选择最多 1 张对方角色，本回合费用 -1",
                    opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
                if (chosen.Count > 0)
                {
                    var tgt = opp.Characters.First(c => c.Id.ToString() == chosen[0]);
                    AtomicOps.AddCostModifier(tgt, -1, KeywordDuration.ThisTurn);
                }
            }
        }

        me.TurnOnceUsed.Add(key);
    }
}
