using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-011 托尼托尼·乔巴（角色）
/// 【阻挡者】（纯关键词，由引擎处理）
/// 【对方的回合中】此角色的力量 +2000。
///
/// 实现说明：
///   - 【阻挡者】为纯关键词，引擎处理，无需脚本。
///   - 【对方的回合中】+2000 为持续/静态力量修正 → 用 ContinuousEffect 注册，
///     仅当当前回合玩家不是本卡持有者（即对方回合）时生效，且只作用于此角色自身。
/// </summary>
public class OP10_011_Chopper : IScriptedEffect
{
    public string CardNumber => "OP10-011";

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
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.CurrentTurnPlayer != owner,
        });

        return Task.CompletedTask;
    }
}
