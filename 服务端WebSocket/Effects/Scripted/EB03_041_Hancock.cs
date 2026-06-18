using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-041 孔雀（角色 / 地 / 海军·利刃 cost4 power6000）
/// 【对方的回合中】我方所有费用不高于6且拥有《利刃》特征的角色力量+2000。（持续 → ContinuousEffect）
/// 【登场时】可以丢弃我方手牌中1张拥有《海军》特征的卡牌：抽取2张卡牌。
///
/// 实现说明：
///   - 持续力量用 ContinuousEffect 注册（仅对方回合中、我方费用≤6 且含《利刃》的角色 +2000）。
///   - 登场段为可选成本（弃 1 张《海军》手牌）；无可弃则不发动。
/// </summary>
public class EB03_041_Hancock : IScriptedEffect
{
    public string CardNumber => "EB03-041";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        // 持续：对方回合中，我方费用≤6 且含《利刃》的角色 +2000
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                s.CurrentTurnPlayer != owner &&
                card.Info.HasKeyword("利刃") &&
                s.CurrentCostOf(sideIdx, card) <= 6,
        });

        // 【登场时】可选成本：弃 1 张《海军》手牌 → 抽 2
        var navy = me.Hand.Where(c => c.Info.HasKeyword("海军")).ToList();
        if (navy.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "孔雀【登场时】：丢弃 1 张《海军》手牌，抽取 2 张卡牌？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = navy.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃 1 张《海军》手牌", navy.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (disc.Count == 0) return;

        var card2 = navy.First(c => c.Id.ToString() == disc[0]);
        AtomicOps.DiscardHand(me, card2);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
    }
}
