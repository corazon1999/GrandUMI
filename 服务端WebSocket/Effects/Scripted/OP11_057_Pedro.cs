using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-057 佩德罗（角色 / 水 / 纯毛族）
/// 我方手牌不多于4张的场合，此角色获得【阻挡者】效果。
///
/// 实现：OnEnterField 注册条件性 GrantKeyword="阻挡者"，谓词限定本卡且我方手牌 ≤4 时生效。
/// </summary>
public class OP11_057_Pedro : IScriptedEffect
{
    public string CardNumber => "OP11-057";
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
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, c) =>
                c.Id == selfId && s.Players[owner].Hand.Count <= 4,
        });
        return Task.CompletedTask;
    }
}
