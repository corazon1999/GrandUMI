using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-060 前进·梅利号（舞台 / 光 / 草帽一伙，cost2）
/// 【启动主要】可以将此舞台转为休息状态，并将我方生命区最上方的 1 张卡牌翻至正面朝上：
///   直到下个对方的回合结束时为止，我方最多 1 张拥有《草帽一伙》特征的角色力量 +1000。
///
/// 实现说明：
///   - 可选成本：将此舞台横置（RestCard）+ 将生命顶 1 张翻正面（仅为成本，向发动者展示其牌面）。
///     需此舞台当前为活跃且有生命牌方可支付。
///   - 收益：选 1 张《草帽一伙》角色，+1000 直到下个对方回合结束 —— ContinuousEffect + TurnCount 限有效期。
/// </summary>
public class EB02_060_GoingMerry : IScriptedEffect
{
    public string CardNumber => "EB02-060";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;

        // 成本需求：此舞台活跃 + 有生命牌
        if (self.IsTapped) return;
        if (me.LifeArea.Count == 0) return;

        var cands = me.Characters.Where(c => c.Info.HasKeyword("草帽一伙")).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "前进·梅利号【启动主要】：横置此舞台并翻开生命顶 1 张，使 1 张《草帽一伙》角色 +1000（直到下个对方回合结束）？");
        if (!use) return;

        // 成本：横置此舞台 + 将生命顶 1 张翻至正面朝上（向发动者展示）
        AtomicOps.RestCard(self);
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new List<string> { me.LifeArea[0].Info.Number });

        // 选 1 张《草帽一伙》角色
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多 1 张《草帽一伙》角色 +1000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;
        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        var tgtId = tgt.Id;

        // 直到下个对方回合结束：ContinuousEffect + TurnCount 限有效期
        int baseTurn = ctx.State.TurnCount;
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = self.Id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner && card.Id == tgtId && s.TurnCount < baseTurn + 2,
        });
    }
}
