using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-086 希流（角色 / 黑(暗) / 4 费 / 5000 / 黑胡子海盗团）
/// 【对方的回合中】此角色的力量 +2000。（持续力量修正 → ContinuousEffect）
/// 【启动主要】【每回合1次】我方领袖拥有《黑胡子海盗团》特征，且在此角色登场的回合的场合，
///   将对方最多 1 张原本的费用不高于 3 的角色 KO。
///
/// 说明 / 简化点：
///   - 【对方的回合中】+2000：注册 ContinuousEffect，仅对方回合中对自身生效，离场自动清理。
///   - "此角色登场的回合"：用 self.TurnPlayed == 当前 TurnCount 判定（登场即记录回合编号）。
///   - "原本的费用不高于 3"：取卡面原始费用 c.Info.Cost。
///   - 【每回合1次】用 me.TurnOnceUsed 去重。
/// </summary>
public class OP10_086_Shiryu : IScriptedEffect
{
    public string CardNumber => "OP10-086";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 持续：【对方的回合中】此角色力量 +2000
            var selfId = self.Id;
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                PowerDelta = 2000,
                Predicate = (s, sideIdx, card) => card.Id == selfId && s.CurrentTurnPlayer != owner,
            });
            return;
        }

        // 【启动主要】【每回合1次】
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 条件：我方领袖《黑胡子海盗团》且此角色为本回合登场
        if (me.Leader == null || !me.Leader.Info.HasKeyword("黑胡子海盗团")) return;
        if (self.TurnPlayed != ctx.State.TurnCount) return;

        var cands = opp.Characters.Where(c => c.Info.Cost <= 3).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张原本费用≤3 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        me.TurnOnceUsed.Add(key);
    }
}
