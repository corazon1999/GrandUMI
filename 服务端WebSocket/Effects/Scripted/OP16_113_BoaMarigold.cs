using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-113 波尔·玛丽格鲁德（角色，光，九蛇海盗团）
/// 持续：我方生命卡牌不多于 2 张的场合，此角色获得【阻挡者】效果。
/// 【触发】我方领袖拥有《九蛇海盗团》特征的场合，此卡牌登场。
///
/// 实现说明：
///   - 条件赋予关键词【阻挡者】用 ContinuousEffect.GrantKeyword 注册（OnEnterField 时机），
///     Predicate 限定仅此卡且我方生命牌 ≤2 时生效；离场由引擎清理。
///   - 【触发】（生命触发）：发动时本卡已在废弃区，若我方领袖具《九蛇海盗团》特征则从废弃区登场。
/// </summary>
public class OP16_113_BoaMarigold : IScriptedEffect
{
    public string CardNumber => "OP16-113";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        // ── 【触发】我方领袖具《九蛇海盗团》特征时，此卡从废弃区登场 ──
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            if (me.Leader.Info.HasKeyword("九蛇海盗团"))
            {
                AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, self);
            }
            return Task.CompletedTask;
        }

        // ── 持续：我方生命牌 ≤2 时，此角色获得【阻挡者】 ──
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, c) =>
                c.Id == selfId && s.Players[owner].LifeCount <= 2,
        });

        return Task.CompletedTask;
    }
}
