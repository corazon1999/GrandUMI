using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-018 KEEP OUT（事件 / 炎 / 1费 / 因佩尔地狱·革命军）
/// 【反击】直到下个我方的回合结束时为止，我方最多1张拥有《革命军》特征的角色力量+2000。
///
/// 实现说明 / 简化点：
///   - 事件本身打出后进入废弃区，无法承载"跨回合"修正；故将持续力量挂在目标角色自身上
///     （SourceCardId = 目标角色 Id，目标在场则效果保留），并用基于回合数的 Predicate 自动到期。
///   - "直到下个我方回合结束时为止"：本卡为【反击】，通常在对方回合发动；到期回合 = 之后第一个我方
///     回合（其结束阶段后失效）。Predicate 在该回合结束（轮到对方回合开始，TurnCount 超过到期回合）后
///     返回 false 自动失活。
///   - 目标离场时引擎会清理该 ContinuousEffect。
/// </summary>
public class OP07_018_KeepOut : IScriptedEffect
{
    public string CardNumber => "OP07-018";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        var cands = me.Characters.Where(c => c.Info.HasKeyword("革命军")).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多 1 张《革命军》角色，直到下个我方回合结束时力量+2000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = cands.First(c => c.Id.ToString() == chosen[0]);

        // 计算到期回合：之后第一个我方回合。当前若已是我方回合则为本回合，否则为下一回合。
        int expireTurn = ctx.State.CurrentTurnPlayer == owner
            ? ctx.State.TurnCount
            : ctx.State.TurnCount + 1;

        var targetId = target.Id;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == targetId.ToString() && e.PowerDelta == 2000);
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = targetId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 2000,
            // 仅作用于目标角色；在到期回合及之前生效，超过后自动失活。
            Predicate = (s, sideIdx, card) => card.Id == targetId && s.TurnCount <= expireTurn,
        });
    }
}
