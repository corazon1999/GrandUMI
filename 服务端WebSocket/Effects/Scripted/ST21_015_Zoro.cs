using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST21-015 罗罗诺亚·佐罗（角色）
/// 【咚!!×2】此角色获得【速攻】效果。（持续 GrantKeyword）
/// （【KO时】从手牌登场红色角色 由 DSL ST21.json 实现，本脚本只处理登场注册持续，OnKO 落回 DSL）
/// </summary>
public class ST21_015_Zoro : IScriptedEffect
{
    public string CardNumber => "ST21-015";
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
            GrantKeyword = "速攻",
            Predicate = (s, side, card) =>
                card.Id == selfId && s.Players[owner].AttachedDonCount(selfId) >= 2,
        });
        return Task.CompletedTask;
    }
}
