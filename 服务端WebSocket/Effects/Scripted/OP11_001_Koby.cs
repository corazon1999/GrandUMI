using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-001 可比（领航 / 炎・地 / 海军・利刃）
/// 我方拥有《利刃》特征的角色可以在登场的回合中攻击角色。
/// 【每回合1次】我方原本的力量不高于7000且拥有《海军》特征的角色因对方效果将要离开场上的场合，
///   可以改为将我方废弃区中的3张卡牌自选顺序放回卡组最下方，以代替离场。
///
/// 实现说明 / 简化点：
///   - 第一段（《利刃》角色登场回合可攻击角色）= 持续静态许可，OnGameStart 注册
///     GrantKeyword="登场回合可攻击角色"，谓词限定我方且角色含《利刃》。ActionValidator 据此放行。
///   - 第二段为"代替离场"的替换型守护效果（将废弃3张放回卡组底以代替离场）。引擎对效果KO/离场的
///     置换守护暂无通道（OnAllyWillBeKOd 仅战斗KO派发，且无"放废弃回卡组代替离场"置换），故本段未实现。
/// </summary>
public class OP11_001_Koby : IScriptedEffect
{
    public string CardNumber => "OP11-001";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnGameStart;

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
            Predicate = (s, sideIdx, c) => sideIdx == owner && c.Info.HasKeyword("利刃"),
        });
        return Task.CompletedTask;
    }
}
