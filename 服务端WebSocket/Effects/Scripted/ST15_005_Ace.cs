using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST15-005 波特夹斯·D·艾斯（角色 / 红 / 5费 6000，白胡子海盗团，SR）
/// 我方领袖的特征中包含《白胡子海盗团》的场合，此角色获得【速攻】。
/// 【每回合1次】此角色将要因对方的效果离开场上时，可以代替使此角色本回合力量-2000。
///
/// 实现：登场时注册条件【速攻】持续效果（ContinuousEffect.GrantKeyword，Predicate 限自身+领袖含白胡子海盗团）。
///   速攻由 ActionValidator.HasKeyword 查询 ContinuousEffect.GrantKeyword 生效（登场回合可攻击）。
/// 简化点：第二段「将要因对方效果离场时代替减2000」(OnAllyWillLeaveField 每回合1次置换) 暂未实现，待后续。
/// （ST15 联网补全：本卡数据据英文卡表翻译，卡图待补、效果待官方校对。）
/// </summary>
public class ST15_005_Ace : IScriptedEffect
{
    public string CardNumber => "ST15-005";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 条件【速攻】：我方领袖特征含《白胡子海盗团》时，此角色获得速攻（登场回合可攻击）
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "速攻",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[owner].Leader.Info.HasKeyword("白胡子海盗团"),
        });

        return Task.CompletedTask;
    }
}
