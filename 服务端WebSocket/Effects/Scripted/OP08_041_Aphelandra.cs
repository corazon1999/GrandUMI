using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-041 阿菲兰朵拉（角色）
/// 【启动主要】可以将此角色放回其持有者的手牌：我方领袖拥有《九蛇海盗团》特征的场合，
///   将对方最多1张费用不高于1的角色放回其持有者的卡组最下方。
///
/// 实现说明 / 简化点：
///   - 成本"将此角色放回持有者手牌"无 DSL cost 通道，脚本用 AtomicOps.BounceToHand 实现。
///   - 仅当我方领袖拥有《九蛇海盗团》特征时本效果才有收益，故仅在该前提下询问是否发动。
/// </summary>
public class OP08_041_Aphelandra : IScriptedEffect
{
    public string CardNumber => "OP08-041";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 仅当我方领袖拥有《九蛇海盗团》特征时本效果有收益
        if (!me.Leader.Info.HasKeyword("九蛇海盗团")) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "阿菲兰朵拉【启动主要】：将此角色放回手牌，将对方最多1张费用≤1的角色放回卡组最下方？");
        if (!use) return;

        // 成本：将此角色放回其持有者（我方）的手牌
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, self);

        // 收益：将对方最多1张费用≤1的角色放回卡组最下方
        var cands = opp.Characters.Where(c => c.Info.Cost <= 1).ToList();
        if (cands.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多1张费用≤1的角色放回卡组最下方",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
