using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-028 强高（角色）
/// 【登场时】选择以下的1项：
///   ・将我方最多1张拥有《东海》特征的领袖或拥有《东海》特征的费用不高于6的角色转为活跃状态。
///   ・将此角色和对方最多1张角色转为休息状态。
///
/// 实现说明：二选一用 ChooseOption。
///   选项1：候选为领袖（含《东海》）或角色（含《东海》且费用≤6），ActivateCard。
///   选项2：横置自身 + 对方最多1张角色横置。
/// </summary>
public class OP03_028_Jango : IScriptedEffect
{
    public string CardNumber => "OP03-028";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "强高【登场时】选择1项",
            new[] { "将我方1张《东海》领袖或《东海》费用≤6角色转为活跃", "将此角色和对方最多1张角色转为休息状态" });

        if (opt == 0)
        {
            var cands = new List<CardInstance>();
            if (me.Leader.Info.HasKeyword("东海")) cands.Add(me.Leader);
            cands.AddRange(me.Characters.Where(c =>
                c.Info.HasKeyword("东海") && ctx.State.CurrentCostOf(ctx.OwnerIndex, c) <= 6));
            // 仅对休息状态的目标有意义
            cands = cands.Where(c => c.IsTapped).ToList();
            if (cands.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "将我方最多1张《东海》领袖或《东海》费用≤6角色转为活跃状态",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.ActivateCard(tgt);
            }
        }
        else
        {
            // 横置自身
            AtomicOps.RestCard(self);
            // 对方最多1张角色横置
            var oppChars = opp.Characters.Where(c => !c.IsTapped).ToList();
            if (oppChars.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多1张角色转为休息状态",
                oppChars.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = oppChars.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.RestCard(tgt);
            }
        }
    }
}
