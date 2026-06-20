using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-024 撒谎布（角色 / 风 4 费 5000）
/// 【对方的回合中】此角色不会因对方领袖和角色的效果而转为休息状态，并获得【阻挡者】效果。（持续状态）
/// 【KO时】将对方最多 1 张领袖或费用不高于 7 的角色转为休息状态。
///
/// 实现说明 / 简化点：
///   - 【对方的回合中】持续获得【阻挡者】：在 OnEnterField 注册 ContinuousEffect.GrantKeyword="阻挡者"，
///     Predicate 限定为"自身 且 当前为对方回合"（规范 13.1）。来源卡离场时引擎自动清理。
///   - 【对方的回合中】"不会因对方领袖和角色的效果而转为休息" 属于条件化(仅对方回合、仅对方效果)的
///     持续免疫，引擎无该类持续通道（RestrictionKind.CannotBeRested 为固定时长且不区分来源，
///     无法表达"仅对方效果导致的休息"），故此细分免疫未实现。
///   - 【KO时】用 OnKO 钩子：从"对方领袖 + 费用≤7 的对方角色"中选最多 1 张转为休息状态（RestCard）。
/// </summary>
public class OP15_024_Usopp : IScriptedEffect
{
    public string CardNumber => "OP15-024";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;

        // ── OnEnterField：注册【对方的回合中】此角色获得【阻挡者】 ──
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var selfId = self.Id;
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "阻挡者",
                Predicate = (s, sideIdx, c) =>
                    c.Id == selfId && s.CurrentTurnPlayer != owner,
            });
            return;
        }

        // ── OnKO ──
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var targets = new List<CardInstance> { opp.Leader };
        targets.AddRange(opp.Characters.Where(c => c.Info.Cost <= 7));
        // 仅保留当前未休息的目标更友好，但保留全部也无害；此处给出全部可选
        targets = targets.Where(c => c is not null).ToList();
        if (targets.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeaderOrCharacter",
            "将对方最多 1 张领袖或费用≤7 的角色转为休息状态",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.RestCard(tgt);
    }
}
