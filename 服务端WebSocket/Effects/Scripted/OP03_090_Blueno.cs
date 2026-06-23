using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects.Dsl;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-090 布鲁诺（角色 / CP9）
/// 【咚!!×1】此角色获得【阻挡者】效果。（被赋予中的咚!! ≥1 时，按附着咚数动态评估）
/// 【KO时】… 由 DSL OP03_wf.json 实现（本脚本不接管 OnKO，退回 DSL）。
///
/// 本脚本：登场时注册条件持续 ContinuousEffect（附着咚≥1 → 获得【阻挡者】），
///   随后委托 DSL 执行该卡【登场时】效果（若有）。
/// </summary>
public class OP03_090_Blueno : IScriptedEffect
{
    public string CardNumber => "OP03-090";
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
            // HasContinuousKeyword 仅按 Predicate 判定、不应用 Scope，须显式限定本卡自身
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.Players[owner].AttachedDonCount(selfId) >= 1,
        });
        await DslInterpreter.TryResolve(ctx);
    }
}
