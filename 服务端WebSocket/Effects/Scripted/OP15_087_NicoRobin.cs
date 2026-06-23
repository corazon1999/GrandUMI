using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects.Dsl;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-087 妮古·罗宾（角色）
/// 我方废弃区中有 10 张或更多卡牌的场合，此角色获得【阻挡者】效果。（按废弃区张数动态评估）
/// 【登场时】抽取 2 张卡牌，丢弃我方的 2 张手牌（DSL OP15.json）。
///
/// 本脚本：登场时注册条件持续 ContinuousEffect（废弃区≥10 → 获得【阻挡者】），
///   随后委托 DSL 执行【登场时】抽 2 丢 2。
/// </summary>
public class OP15_087_NicoRobin : IScriptedEffect
{
    public string CardNumber => "OP15-087";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.Players[owner].Trash.Count >= 10,
        });
        await DslInterpreter.TryResolve(ctx);
    }
}
