using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-024 莫比·迪克号（舞台 / 炎 / 白胡子海盗团）
/// 【我方的回合中】我方的生命卡牌不多于1张的场合，
///   我方的"爱德华·纽哥特"和所有拥有的特征中包含〈白胡子海盗团〉的角色力量+2000。
///
/// 实现说明：持续力量修正，用 ContinuousEffect.PowerDelta 注册（登场时）。
///   条件：当前为我方回合、我方生命 ≤1；作用对象=名为"爱德华·纽哥特"或含〈白胡子海盗团〉的我方角色。
/// </summary>
public class OP02_024_MobyDick : IScriptedEffect
{
    public string CardNumber => "OP02-024";

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
                Filter = c => c.Info.NameContains("爱德华·纽哥特") || c.Info.HasKeyword("白胡子海盗团"),
            },
            PowerDelta = 2000,
            Predicate = (s, sideIdx, c) =>
                s.CurrentTurnPlayer == owner &&
                s.Players[owner].LifeCount <= 1,
        });

        return Task.CompletedTask;
    }
}
