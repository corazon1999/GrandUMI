using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-023 卡里布（角色）
/// 持续：我方场上存在 6 张或更多休息状态的咚!! 的场合，此角色的力量 +1000。
///   （通过 ContinuousEffect 注册，引擎每次评估力量时按条件累加。）
/// 【阻挡者】为纯关键词，由引擎处理，脚本不实现。
/// </summary>
public class OP07_023_Caribou : IScriptedEffect
{
    public string CardNumber => "OP07-023";

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
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[owner].RestDonCount >= 6,
        });

        return Task.CompletedTask;
    }
}
