using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-101 杰丽·邦妮（3 费 1000 黄色 / 超新星·邦妮海盗团）
/// 【触发】我方领袖拥有《超新星》特征的场合，此卡牌登场。
/// 【启动主要】可以将此角色转为休息状态：
///   直到下个对方的回合结束时为止，我方拥有《超新星》特征的领袖力量 +1000。
///
/// 实现：
///   - 【触发】(OnLifeRevealTrigger)：生命牌触发时此卡已被放入废弃区（见 LifeRevealManager），
///     故此处从废弃区免费登场；仅当我方领袖拥有《超新星》特征时才登场（否则留在废弃区）。
///   - 【启动主要】(ActivatedMain)：代价为将此角色转为休息状态（自身需为活跃）；
///     若我方领袖拥有《超新星》特征，则领袖力量 +1000。
///
/// 简化点：
///   - "直到下个对方的回合结束时为止"用 AddPowerThisTurn 近似（引擎无该精确持续力量通道，
///     PowerModThisTurn 在本回合结束阶段清除）。对己方回合内的进攻配合实战意义一致。
/// </summary>
public class OP12_101_Bonney : IScriptedEffect
{
    public string CardNumber => "OP12-101";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnLifeRevealTrigger || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 条件：我方领袖拥有《超新星》特征
            if (!me.Leader.Info.HasKeyword("超新星")) return;
            // 此卡已在废弃区，从废弃区免费登场
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source, restState: false);
            await Task.CompletedTask;
            return;
        }

        if (ctx.Trigger == EffectTrigger.ActivatedMain)
        {
            // 代价：将此角色转为休息状态（需活跃）
            if (ctx.Source.IsTapped) return;
            ctx.Source.IsTapped = true;

            // 效果：我方领袖拥有《超新星》特征时，领袖力量 +1000（近似为本回合）
            if (me.Leader.Info.HasKeyword("超新星"))
                AtomicOps.AddPowerThisTurn(me.Leader, 1000);

            await Task.CompletedTask;
        }
    }
}
