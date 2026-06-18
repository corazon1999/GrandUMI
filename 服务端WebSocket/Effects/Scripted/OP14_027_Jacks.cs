using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-027 杰克斯（角色，风）
/// 【我方的回合中】当此角色转为休息状态时，将对方最多 1 张原本的力量不高于 7000 的角色转为休息状态。
/// 【对方的回合中】此角色处于休息状态的场合，对方的所有角色力量 -1000。
///
/// 实现说明：
///   - 第 1 段用 OnCharRested watcher 实现（仅当被横置的是本卡自身、且为我方回合时触发）；
///     "原本的力量"取卡面 c.Info.Power；"转为休息状态"用 AtomicOps.RestCard。
///   - 第 2 段为持续力量修正，在 OnEnterField 时机注册 ContinuousEffect：
///     Side=1（对方全体），Predicate = 对方回合中 且 本卡处于休息状态，PowerDelta=-1000。
/// </summary>
public class OP14_027_Jacks : IScriptedEffect
{
    public string CardNumber => "OP14-027";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnCharRested;

    public async Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        // ── 【对方的回合中】此角色休息时，对方全体 -1000（持续，登场时注册）──
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true },
                PowerDelta = -1000,
                Predicate = (s, sideIdx, card) =>
                    s.CurrentTurnPlayer != owner && self.IsTapped,
            });
            return;
        }

        // ── 【我方的回合中】当此角色转为休息状态时 ──
        if (ctx.State.CurrentTurnPlayer != owner) return;
        var restedId = ctx.Vars.TryGetValue("restedCardId", out var v) ? v as string : null;
        if (restedId != selfId.ToString()) return; // 仅本卡被横置

        var opp = ctx.State.Players[1 - owner];
        var cands = opp.Characters.Where(c => c.Info.Power <= 7000 && !c.IsTapped).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(owner, "OpponentCharacter",
            "将对方最多 1 张原本力量≤7000 的角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
