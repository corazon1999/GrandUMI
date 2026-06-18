using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-027 甚平（角色 / 风 / 鱼人族・因佩尔地狱・太阳海盗团，cost2 power2000）
/// 【咚!!×1】此角色的力量+2000。
///
/// 实现说明（持续/静态力量修正）：
///   - 依据"被赋予中的咚!! 数量≥1"对自身 +2000，用 ContinuousEffect 注册（OnEnterField）。
///   - Predicate 仅对本卡生效，且要求该卡当前被赋予的咚!! 数量 ≥1。
///   - 离场后由引擎自动清理；重复登场前先去重避免叠加。
/// </summary>
public class OP16_027_Jinbe : IScriptedEffect
{
    public string CardNumber => "OP16-027";

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
                card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 1,
        });
        return Task.CompletedTask;
    }
}
