using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-051 埃米特（角色 / 光 / 艾格赫德，cost8 power7000）
/// 本体：场上不存在原本的力量为 12000 或更高的角色的场合，此角色无法攻击。
/// 【触发】本回合中，对方所有角色力量 -3000。之后，我方生命卡牌为 0 张的场合，此卡牌登场。
///
/// 实现说明：
///   - 本体（M1 条件静态禁攻）：OnEnterField 注册 ContinuousEffect.GrantRestriction=CannotAttack，
///     谓词成立（双方场上都不存在原本力量≥12000角色）时此卡无法攻击。
///   - 【触发】（M7）：生命牌触发时此卡已在废弃区；对方所有角色本回合 -3000（AddPowerToAllThisTurn），
///     之后若我方生命为 0，则用 PlayFromTrashFree 将此卡（ctx.Source，当前在废弃区）登场。
/// </summary>
public class EB04_051_Emeth : IScriptedEffect
{
    public string CardNumber => "EB04-051";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var selfId = ctx.Source.Id;
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantRestriction = RestrictionKind.CannotAttack,
                Predicate = (s, sideIdx, card) =>
                    card.Id == selfId &&
                    !s.Players[0].Characters.Concat(s.Players[1].Characters).Any(x => x.Info.Power >= 12000),
            });
            return Task.CompletedTask;
        }

        // OnLifeRevealTrigger：对方所有角色本回合 -3000
        var me = ctx.State.Players[ctx.OwnerIndex];
        AtomicOps.AddPowerToAllThisTurn(ctx.State, 1 - ctx.OwnerIndex, _ => true, -3000, includeLeader: false);

        // 之后：我方生命为 0 时，此卡（当前在废弃区）登场
        if (me.LifeArea.Count == 0)
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source, restState: false);

        return Task.CompletedTask;
    }
}
