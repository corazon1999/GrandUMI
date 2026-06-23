using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-004 杰克斯（角色）
/// 持续：对方所有角色力量 -1000。（在场即生效的群体光环 → ContinuousEffect 注册）
/// 【速攻】由引擎按关键词处理，本脚本不处理。
/// </summary>
public class OP09_004_Shanks : IScriptedEffect
{
    public string CardNumber => "OP09-004";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;
        int oppSide = 1 - owner;

        // 防重复注册
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 对方所有角色力量 -1000（恒定生效）
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = -1000,
            // Scope 仅供显示，作用对象必须写进 Predicate：仅对方场上角色（不含领袖、不含己方）。
            // 否则 Predicate=true 会减全场两侧角色与领袖（#149）。
            Predicate = (s, sideIdx, c) =>
                sideIdx == oppSide &&
                s.Players[oppSide].Characters.Contains(c),
        });

        return Task.CompletedTask;
    }
}
