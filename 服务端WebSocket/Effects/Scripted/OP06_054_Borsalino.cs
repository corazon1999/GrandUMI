using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-054 波尔萨利诺（角色 / 水 2 费 4000 / 海军）
/// 我方手牌不多于 5 张的场合，此角色获得【阻挡者】效果。
///
/// 实现：注册条件性 GrantKeyword="阻挡者" 的持续效果，谓词限定本卡自身，
/// 条件为我方手牌 ≤5 张。
/// </summary>
public class OP06_054_Borsalino : IScriptedEffect
{
    public string CardNumber => "OP06-054";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner && c.Id == selfId &&
                s.Players[owner].Hand.Count <= 5,
        });

        return Task.CompletedTask;
    }
}
