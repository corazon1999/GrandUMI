using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-014 霍怀迪贝（角色 / 炎 / 白胡子海盗团旗下）
/// 【咚!!×1】此角色也可以攻击对方活跃状态的角色。
///
/// 实现说明：用 ContinuousEffect.GrantKeyword="可攻击活跃" + Predicate 注册（Wave3）。
///   条件：被赋予的咚 ≥1 时自身获得该能力。ActionValidator 据此放行攻击对方活跃角色。
/// </summary>
public class OP02_014_Whitey : IScriptedEffect
{
    public string CardNumber => "OP02-014";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "可攻击活跃",
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner && c.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 1,
        });

        return Task.CompletedTask;
    }
}
