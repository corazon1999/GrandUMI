using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-042 爱比达
/// 【登场时】将对方最多 1 张原本的费用不高于 1 的角色放回其持有者的卡组最下方。
///
/// - 卡面常驻被动"我方场上存在 2 张或更多原本的费用为 5 或更高的角色的场合，此角色的费用+1"
///   通过条件式持续费用修正实现。
/// - 候选用"原本的费用"= CardInfo.Cost 过滤（≤1）。
/// </summary>
public class OP12_042_Abida : IScriptedEffect
{
    public string CardNumber => "OP12-042";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var selfId = ctx.Source.Id;
        int ownerIdx = ctx.OwnerIndex;
        s.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        s.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            CostDelta = 1,
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true, Filter = c => c.Id == selfId },
            Predicate = (state, side, card) => card.Id == selfId
                && state.Players[ownerIdx].Characters.Count(c => c.Info.Cost >= 5) >= 2,
        });
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = s.Players[oppIdx];

        // 对方原本费用不高于 1 的角色
        var candidates = opp.Characters.Where(c => c.Info.Cost <= 1).ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张原本费用不高于 1 的角色放回卡组最下方",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is null) return;

        AtomicOps.ReturnFieldToDeckBottom(s, oppIdx, target);
    }
}
