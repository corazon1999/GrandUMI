using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST05-010 泽法（角色）
/// 当此角色与拥有属性(打)的角色战斗时，本回合中，此角色力量+3000。（持续，战斗条件）
/// （【启动主要】咚-1：此角色+2000 由 DSL ST05.json 实现，本脚本只处理登场注册持续战斗加成）
/// </summary>
public class ST05_010_Zephyr : IScriptedEffect
{
    public string CardNumber => "ST05-010";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 3000,
            Predicate = (s, sideIdx, card) =>
            {
                if (card.Id != selfId) return false;
                var b = s.CurrentBattle;
                if (b is null) return false;
                // 找到本次战斗中“对手”那张卡，检查其属性是否为 打
                CardInstance? foe = null;
                if (b.AttackerCardId == selfId)
                {
                    if (b.TargetIsLeader) return false; // 对手是领袖，非“角色”
                    var did = b.ReplacedByBlockerCardId ?? b.TargetCardId;
                    if (did is null) return false;
                    foe = s.Players[b.DefenderPlayerIndex].Characters.FirstOrDefault(c => c.Id == did);
                }
                else if ((b.ReplacedByBlockerCardId ?? b.TargetCardId) == selfId)
                {
                    var atk = s.Players[b.AttackerPlayerIndex];
                    foe = atk.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
                }
                return foe is not null && foe.Info.Property == "打";
            },
        });
        return Task.CompletedTask;
    }
}
