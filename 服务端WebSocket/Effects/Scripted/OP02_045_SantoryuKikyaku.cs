using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-045 三刀流 鬼斩（事件 / 风 cost3）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +6000。
///   之后，将我方手牌中最多 1 张费用不高于 3 且原本没有效果的角色卡牌登场。
/// 【触发】将对方最多 1 张领袖或费用不高于 5 的角色转为休息状态。
///
/// 实现说明：
///   - 【反击】(EventCounter)：先选 1 张我方领袖/角色 +6000(本次战斗)；之后登场 cost≤3 且「原本没有效果」的角色。
///   - 「原本没有效果」判定：无 EffectTags、无 Abilities、无 Trigger 文本。
///   - 【触发】(OnLifeRevealTrigger)：将对方最多 1 张领袖或费用≤5 角色转为休息状态。
/// </summary>
public class OP02_045_SantoryuKikyaku : IScriptedEffect
{
    public string CardNumber => "OP02-045";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    private static bool HasNoOriginalEffect(CardInstance c) =>
        c.Info.EffectTags.Length == 0 && c.Info.Abilities.Length == 0 && string.IsNullOrEmpty(c.Info.Trigger);

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 【触发】将对方最多 1 张领袖或费用≤5 角色转为休息状态
            var cands = new List<CardInstance> { opp.Leader };
            cands.AddRange(opp.Characters.Where(c => c.Info.Cost <= 5));
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeaderOrCharacter",
                "将对方最多 1 张领袖或费用≤5 角色转为休息状态",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.RestCard(tgt);
            }
            return;
        }

        // 【反击】本次战斗中，我方最多 1 张领袖或角色 +6000
        var buffTargets = new List<CardInstance> { me.Leader };
        buffTargets.AddRange(me.Characters);
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本次战斗中，我方最多 1 张领袖或角色力量 +6000",
            buffTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var tgt = buffTargets.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisBattle(tgt, 6000);
        }

        // 之后：登场最多 1 张 cost≤3 且原本没有效果的角色
        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.Cost <= 3 && HasNoOriginalEffect(c)).ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场最多 1 张 费用≤3 且原本没有效果的角色",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen2.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen2[0]);
            AtomicOps.PlayFromHandFree(ctx.State, owner, picked);
        }
    }
}
