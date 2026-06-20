using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-096 ……什么事……也没有！！（事件 / 地 3 费 / 草帽一伙）
/// 【反击】可以将我方生命区最上方的 1 张卡牌加入手牌：
///   本回合中，我方所有费用不高于 7 的角色在战斗中不会被 KO。
/// 【触发】发动此卡牌的【反击】效果。
///
/// 实现说明 / 简化点：
///   - 成本"将我方生命区最上方的 1 张加入手牌"为可选（"可以"），用 ConfirmOptional 询问；
///     生命区为空则无法支付成本 → 不发动。
///   - "本回合中…费用≤7 角色在战斗中不会被 KO" → 注册 KoGuard="battle" 的 ContinuousEffect，
///     Predicate 限定为：我方一侧、当前费用≤7、且在本回合内有效（用 TurnCount 基准限制有效期）。
///   - 事件卡本身在结算后进废弃区，但 ContinuousEffect 以 SourceCardId 关联；为避免来源离场被清理，
///     基准回合数限制其仅在本回合生效，回合切换后谓词恒为 false（且引擎不再因来源在场而保留）。
///     为稳妥起见仍用一个固定的源标识并在过期时不再生效。
///   - 【反击】与【触发】共用同一收益实现（OnLifeRevealTrigger 走同一逻辑）。
/// </summary>
public class OP06_096_NothingHappened : IScriptedEffect
{
    public string CardNumber => "OP06-096";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        // 成本：可以将我方生命区最上方的 1 张卡牌加入手牌
        if (me.LifeArea.Count > 0)
        {
            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "……什么事……也没有！！：将我方生命区最上方的 1 张卡牌加入手牌，使本回合费用≤7 的角色战斗中不会被 KO？");
            if (use)
            {
                var top = me.LifeArea[0];
                me.LifeArea.RemoveAt(0);
                me.Hand.Add(top);
            }
            else
            {
                return; // 不支付成本则不发动收益
            }
        }
        else
        {
            // 生命区为空，无法支付成本
            return;
        }

        // 收益：本回合中，我方所有费用≤7 的角色在战斗中不会被 KO
        int baseTurn = ctx.State.TurnCount;
        var srcId = ctx.Source.Id.ToString();
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == srcId);
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = srcId,
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "battle",
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                s.TurnCount == baseTurn &&
                s.CurrentCostOf(sideIdx, card) <= 7,
        });
    }
}
