using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-110 火特&瓦亚（角色）
/// 【登场时】将对方最多 1 张费用不高于"对方生命卡牌张数"的角色转为休息状态。
///   （【触发】部分由引擎在生命触发流程中处理，此处仅实现【登场时】主效果。）
///
/// 实现说明：
///   - 阈值为动态值 = 对方生命区张数 opp.LifeArea.Count，在脚本中即时计算。
///   - "转为休息"用 AtomicOps.RestCard。
/// </summary>
public class OP10_110_HotoriKotori : IScriptedEffect
{
    public string CardNumber => "OP10-110";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int threshold = opp.LifeArea.Count;

        var cands = opp.Characters.Where(c => c.Info.Cost <= threshold).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            $"选择最多 1 张费用不高于 {threshold} 的对方角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
