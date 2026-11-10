using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-004 杰克斯（角色）
/// 持续：对方所有角色力量 -1000。（在场即生效的群体光环 → ContinuousEffect 注册）
/// 【速攻】由引擎按关键词处理，本脚本不处理。
/// </summary>
public class OP09_004_Shanks : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "OP09-004";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task RegisterFieldStatic(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        int oppSide = 1 - owner;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = -1000,
            Predicate = (s, sideIdx, c) =>
                sideIdx == oppSide && s.Players[oppSide].Characters.Contains(c),
        });
        return Task.CompletedTask;
    }

    public Task Resolve(EffectContext ctx)
    {
        RegisterFieldStatic(ctx);

        return Task.CompletedTask;
    }
}
