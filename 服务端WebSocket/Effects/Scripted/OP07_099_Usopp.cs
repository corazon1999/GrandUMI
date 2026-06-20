using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-099 撒谎布（角色 / 光 1 费 2000，艾格赫德/草帽一伙）
/// 【触发】直到下个我方的回合结束时为止，我方最多 1 张拥有《艾格赫德》特征的领袖或角色力量+2000。
///
/// 实现说明 / 简化点：
///   - 【触发】= 生命牌触发 OnLifeRevealTrigger。
///   - 「直到下个我方回合结束」用基于回合数的 Predicate 自动到期（参考 OP07-018）。生命触发在对方回合
///     发动，到期回合 = 之后第一个我方回合。
///   - 持续力量挂在目标自身（SourceCardId = 目标 Id），目标离场时引擎清理。
///   - 目标可为领袖或角色，故 Scope.IncludeLeader = true。
/// </summary>
public class OP07_099_Usopp : IScriptedEffect
{
    public string CardNumber => "OP07-099";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        var cands = new List<CardInstance>();
        if (me.Leader.Info.HasKeyword("艾格赫德")) cands.Add(me.Leader);
        cands.AddRange(me.Characters.Where(c => c.Info.HasKeyword("艾格赫德")));
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张《艾格赫德》领袖或角色，直到下个我方回合结束力量+2000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = cands.First(c => c.Id.ToString() == chosen[0]);

        int expireTurn = ctx.State.CurrentTurnPlayer == owner
            ? ctx.State.TurnCount
            : ctx.State.TurnCount + 1;

        var targetId = target.Id;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == targetId.ToString() && e.PowerDelta == 2000);
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = targetId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = true },
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) => card.Id == targetId && s.TurnCount <= expireTurn,
        });
    }
}
