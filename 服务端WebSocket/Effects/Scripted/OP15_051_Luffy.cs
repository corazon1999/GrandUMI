using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-051 蒙奇·D·路飞（水 3 费 4000，德莱斯罗兹/草帽一伙）
/// 【对方的回合中】我方领袖拥有《德莱斯罗兹》特征的场合，此角色的力量 +3000。
///
/// 实现说明：纯持续/静态力量修正，用 ContinuousEffect 注册（登场时注册，离场由引擎清理）。
///   Predicate：仅在"对方回合中"(CurrentTurnPlayer != owner) 且"我方领袖含《德莱斯罗兹》特征"时，
///   对自身 +3000。来源卡离场时引擎自动清理；重复登场前先按 SourceCardId 去重避免叠加。
/// </summary>
public class OP15_051_Luffy : IScriptedEffect
{
    public string CardNumber => "OP15-051";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        int ownerIndex = ctx.OwnerIndex;
        var selfId = self.Id;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 3000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer != ownerIndex &&
                s.Players[ownerIndex].Leader.Info.HasKeyword("德莱斯罗兹"),
        });

        return Task.CompletedTask;
    }
}
