using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-004 克洛克达尔（角色 / 水 / 王下七武海・巴洛克工作室，cost4 power5000）
/// 【咚!!×1】此角色获得【阻挡者】效果。
///
/// 实现：被赋予中的咚!! ≥1 时，自身获得【阻挡者】，用 ContinuousEffect.GrantKeyword 注册（OnEnterField）。
/// </summary>
public class P_004_Crocodile : IScriptedEffect
{
    public string CardNumber => "P-004";

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
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.Players[owner].AttachedDonCount(selfId) >= 1,
        });

        return Task.CompletedTask;
    }
}
