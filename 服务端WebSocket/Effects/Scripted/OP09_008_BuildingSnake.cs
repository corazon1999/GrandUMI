using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-008 比尔丁格·斯内克（角色）
/// 【启动主要】可以将此角色放回其持有者的卡组最下方：
///   本回合中，对方最多 1 张角色力量 -3000。
///
/// 说明 / 简化点：
/// - 发动成本为"将此角色放回卡组最下方"（与 OP12-080 巴拉蒂同类，DSL cost 节无此键，用脚本实现）。
/// - "可以…"为可选，先 ConfirmOptional 询问；同意后支付成本再结算。
/// </summary>
public class OP09_008_BuildingSnake : IScriptedEffect
{
    public string CardNumber => "OP09-008";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "比尔丁格·斯内克【启动主要】：将此角色放回卡组最下方，使对方 1 张角色本回合 -3000？");
        if (!use) return;

        // 成本：将此角色放回其持有者卡组最下方
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, ctx.Source);

        // 效果：对方最多 1 张角色 -3000
        var cands = opp.Characters.ToList();
        if (cands.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方 1 张角色本回合 -3000（最多 1 张）",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, -3000);
        }
    }
}
