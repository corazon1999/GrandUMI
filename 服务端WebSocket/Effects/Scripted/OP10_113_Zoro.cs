using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-113 罗罗诺亚·佐罗（角色 / 光 3 费 5000，超新星·草帽一伙）
///
/// 完整文本：
///   我方生命卡牌的张数少于对方生命卡牌的张数的场合，此角色获得【速攻】效果。
///   【触发】可以丢弃我方的 1 张手牌：我方领袖拥有《超新星》特征的场合，此卡牌登场。
///
/// 实现：
///   - 持续条件获得【速攻】：在【登场时】用 ContinuousEffect.GrantKeyword 注册，
///     Predicate 限定为本卡且“我方生命数 < 对方生命数”时生效（规范 13.1）。来源卡离场引擎自动清理。
///   - 【触发】：本卡为生命牌触发，发动时已被放入废弃区。
///     可选成本“丢弃我方 1 张手牌”用 ConfirmOptional + ChooseCards 实现；
///     仅当我方领袖拥有《超新星》特征时此卡才从废弃区免费登场。
///
/// 简化点：无。
/// </summary>
public class OP10_113_Zoro : IScriptedEffect
{
    public string CardNumber => "OP10-113";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 持续：我方生命 < 对方生命时，本角色获得【速攻】
            var selfId = self.Id;
            s.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            s.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "速攻",
                Predicate = (st, sideIdx, card) =>
                    sideIdx == owner && card.Id == selfId &&
                    st.Players[owner].LifeCount < st.Players[1 - owner].LifeCount,
            });
            return;
        }

        // ── 【触发】 ──
        // 触发发动时本卡已被放入废弃区
        if (!me.Trash.Contains(self)) return;

        // 仅当我方领袖拥有《超新星》特征时本效果才有收益
        if (!me.Leader.Info.HasKeyword("超新星")) return;

        // 可选成本：丢弃我方 1 张手牌
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "佐罗【触发】：丢弃我方 1 张手牌，使此卡从废弃区登场？");
        if (!use) return;

        var discard = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方 1 张手牌（佐罗登场成本）",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (discard.Count < 1) return;

        var discardCard = me.Hand.First(c => c.Id.ToString() == discard[0]);
        AtomicOps.DiscardHand(me, discardCard);

        // 收益：此卡牌从废弃区登场（以活跃状态）
        if (me.Trash.Contains(self))
            AtomicOps.PlayFromTrashFree(s, ctx.OwnerIndex, self, restState: false);
    }
}
