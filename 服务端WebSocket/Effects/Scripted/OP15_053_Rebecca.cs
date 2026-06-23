using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects.Dsl;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-053 莉贝卡（角色 / 德莱斯罗兹）
/// 【咚!!×1】此角色获得【阻挡者】效果。（被赋予中的咚!! ≥1 时，按附着咚数动态评估）
/// 【登场时】确认我方卡组最上方 3 张，公开其中最多 1 张《德莱斯罗兹》特征的卡牌加入手牌，余下放回卡组最下（DSL OP15.json）。
///
/// 本脚本：登场时注册条件持续 ContinuousEffect（附着咚≥1 → 获得【阻挡者】），
///   随后委托 DSL 执行【登场时】确认顶 3 效果。
/// </summary>
public class OP15_053_Rebecca : IScriptedEffect
{
    public string CardNumber => "OP15-053";
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
                card.Id == selfId && s.Players[owner].AttachedDonCount(selfId) >= 1,
        });
        await DslInterpreter.TryResolve(ctx);
    }
}
