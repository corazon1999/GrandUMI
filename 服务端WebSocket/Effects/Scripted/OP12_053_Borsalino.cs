using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-053 波尔萨利诺（角色）
/// 【对方的回合中】我方领袖拥有《海军》特征的场合，此角色力量+1000、并获得【阻挡者】效果。
///   —— 通过单个 ContinuousEffect 注册（PowerDelta+1000 + GrantKeyword="阻挡者"），同一条件评估。
///
/// 简化点：文本另含【每回合1次】"因对方效果将要离场时可改为弃1手牌使此角色不离场"的
///   离场替代效果（无对应触发钩子），未实现。
/// </summary>
public class OP12_053_Borsalino : IScriptedEffect
{
    public string CardNumber => "OP12-053";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var selfId = self.Id;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 1000,
            GrantKeyword = "阻挡者",   // 同一条件（对方回合+海军领袖）下亦获得【阻挡者】
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer != owner &&
                s.Players[owner].Leader.Info.HasKeyword("海军"),
        });

        return Task.CompletedTask;
    }
}
