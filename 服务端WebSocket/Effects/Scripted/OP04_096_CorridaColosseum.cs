using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-096 斗牛竞技场（舞台 / 地）
/// 我方领袖拥有《德莱斯罗兹》特征的场合，我方拥有《德莱斯罗兹》特征的角色在登场的回合中可以攻击角色。
///
/// 实现：OnEnterField（舞台登场）注册持续 GrantKeyword="登场回合可攻击角色"，谓词限定我方、领袖含
/// 《德莱斯罗兹》、且该角色含《德莱斯罗兹》。ActionValidator 据此放行其登场回合攻击对方角色（不含领袖）。
/// </summary>
public class OP04_096_CorridaColosseum : IScriptedEffect
{
    public string CardNumber => "OP04-096";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var sid = self.Id.ToString();

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == sid);
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = sid,
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "登场回合可攻击角色",
            Predicate = (s, sideIdx, c) => sideIdx == owner
                && s.Players[owner].Leader.Info.HasKeyword("德莱斯罗兹")
                && c.Info.HasKeyword("德莱斯罗兹"),
        });
        return Task.CompletedTask;
    }
}
