using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST08-002 乌塔（角色）
/// 此角色在与领袖的战斗中不会被KO。（持续 KoGuard：battle，仅当对手是领袖）
/// （【启动主要】横置自身：对方角色费用-2 由 DSL ST08.json 实现，本脚本只处理登场注册持续）
/// </summary>
public class ST08_002_Uta : IScriptedEffect
{
    public string CardNumber => "ST08-002";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "battle",
            Predicate = (s, sideIdx, card) =>
            {
                if (card.Id != selfId) return false;
                var b = s.CurrentBattle;
                if (b is null) return false;
                // 自身为攻击方且目标是领袖
                if (b.AttackerCardId == selfId) return b.TargetIsLeader;
                // 自身为防守方且攻击者是对方领袖
                if ((b.ReplacedByBlockerCardId ?? b.TargetCardId) == selfId)
                    return b.AttackerCardId == s.Players[b.AttackerPlayerIndex].Leader.Id;
                return false;
            },
        });
        return Task.CompletedTask;
    }
}
