using System.Linq;
using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST22-015 因为我是白胡子!!（事件，8费）
/// 【主要】我方领袖含〈白胡子海盗团〉的场合，将我方手牌中最多1张"爱德华·纽哥特"登场。
///   之后，可以将我方生命区最上方或最下方的1张卡牌加入手牌。那样做的场合，直到下个对方的回合结束时为止，
///   我方领袖力量+2000。
///
/// 实现：
///   - 登场"爱德华·纽哥特"用 PlayFromHandFree，其【登场时】由批次1（卡效登场触发登场时）兜底发动。
///   - "可选生命入手→那样做才+2000"是可选后接条件加成，DSL 无干净门控，故用脚本。
///   - +2000 限期"直到下个对方回合结束"用 P-107 式 TurnCount 限期持续效果；源挂领袖（领袖恒在场，
///     不会被"源离场"清理；而事件本身打出后入废弃区，不能作源）。
/// </summary>
public class ST22_015_WhitebeardEvent : IScriptedEffect
{
    public string CardNumber => "ST22-015";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Leader.Info.HasKeyword("白胡子海盗团")) return;

        // 将手牌中最多1张"爱德华·纽哥特"登场
        var cands = me.Hand.Where(c => c.Info.Kind == CardKind.Character && c.MatchesName("爱德华·纽哥特")).ToList();
        if (cands.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandWhitebeard",
                "将我方手牌中最多1张\"爱德华·纽哥特\"登场", cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (pick.Count > 0)
            {
                var picked = cands.First(c => c.Id.ToString() == pick[0]);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked); // 其登场时由批次1兜底触发
            }
        }

        // 之后：可以将生命区最上方或最下方1张加入手牌
        if (me.LifeArea.Count == 0) return;
        if (ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) return; // ST15-001 限制

        int choice = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "可以将我方生命区1张加入手牌（那样做则我方领袖+2000直到下个对方回合结束）",
            new List<string> { "生命区最上方", "生命区最下方", "不发动" });
        if (choice == 2) return; // 不发动

        var lifeCard = choice == 0 ? me.LifeArea[0] : me.LifeArea[me.LifeArea.Count - 1];
        me.LifeArea.Remove(lifeCard);
        lifeCard.IsLifeFaceUp = false;
        me.Hand.Add(lifeCard);

        // 那样做 → 直到下个对方回合结束我方领袖+2000（TurnCount 限期；源挂领袖以免被"源离场"清理）
        var leaderId = me.Leader.Id;
        int owner = ctx.OwnerIndex;
        int baseTurn = ctx.State.TurnCount;
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = leaderId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            PowerDelta = 2000,
            Predicate = (s, side, c) => side == owner && c.Id == leaderId && s.TurnCount <= baseTurn + 1,
        });
    }
}
