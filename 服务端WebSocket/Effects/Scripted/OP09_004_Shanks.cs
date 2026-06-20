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

        // 防重复注册
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 对方所有角色力量 -1000（恒定生效）
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = -1000,
            Predicate = (s, sideIdx, c) => true,
        });

        return Task.CompletedTask;
    }
}
