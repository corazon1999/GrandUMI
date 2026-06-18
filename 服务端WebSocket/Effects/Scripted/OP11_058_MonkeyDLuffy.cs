using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-058 蒙奇·D·路飞（角色 / 水 / 草帽一伙）
/// 我方持有5张或更多手牌的场合，此角色无法攻击。
/// 【阻挡者】（引擎关键词，自动处理）
///
/// 实现：OnEnterField 注册条件性 GrantRestriction=CannotAttack，谓词限定本卡且我方手牌 ≥5 时生效。
/// </summary>
public class OP11_058_MonkeyDLuffy : IScriptedEffect
{
    public string CardNumber => "OP11-058";
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
            GrantRestriction = RestrictionKind.CannotAttack,
            Predicate = (s, sideIdx, c) =>
                c.Id == selfId && s.Players[owner].Hand.Count >= 5,
        });
        return Task.CompletedTask;
    }
}
