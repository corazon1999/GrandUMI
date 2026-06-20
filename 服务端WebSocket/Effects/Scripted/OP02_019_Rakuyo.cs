using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-019 拉克约（角色 / 炎 / 白胡子海盗团）
/// 【咚!!×1】【我方的回合中】我方所有拥有的特征中包含〈白胡子海盗团〉的角色力量+1000。
///
/// 实现说明：持续力量修正，用 ContinuousEffect.PowerDelta 注册（登场时）。
///   条件：被赋予的咚 ≥1、且当前为我方回合；作用于我方含〈白胡子海盗团〉的角色。
/// </summary>
public class OP02_019_Rakuyo : IScriptedEffect
{
    public string CardNumber => "OP02-019";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = c => c.Info.HasKeyword("白胡子海盗团"),
            },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, c) =>
                s.CurrentTurnPlayer == owner &&
                s.Players[owner].AttachedDonCount(selfId) >= 1,
        });

        return Task.CompletedTask;
    }
}
