using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-115 波尔·汉库珂（角色 / 光）
/// 【登场时】赋予我方 1 张领袖或角色最多 1 张休息状态的咚!!。
/// 【触发】将我方手牌中最多 1 张力量不高于 5000 且拥有【触发】效果的黄色角色卡牌登场。
///
/// 实现说明：
///   - 【登场时】(OnEnterField)：玩家选 0..1 张领袖/角色，AttachDonFromCost 贴 1 张休息咚。
///   - 【触发】(OnLifeRevealTrigger)："拥有【触发】效果"用 c.Info.Trigger 非空判定；
///     "黄色"用 ColorList.Contains("黄")；力量取卡面原始 Power。玩家选 0..1 张登场。
/// </summary>
public class P_115_BoaHancock : IScriptedEffect
{
    public string CardNumber => "P-115";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 赋予我方 1 张领袖或角色最多 1 张休息状态的咚!!
            var donTargets = new List<CardInstance> { me.Leader };
            donTargets.AddRange(me.Characters);
            var pickTgt = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "选择 1 张领袖或角色，赋予最多 1 张休息状态的咚!!",
                donTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pickTgt.Count > 0)
            {
                var target = donTargets.First(c => c.Id.ToString() == pickTgt[0]);
                AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Rest);
            }
            return;
        }

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 将手牌中最多 1 张力量≤5000 且拥有【触发】的黄色角色登场
            var playable = me.Hand.Where(c =>
                c.Info.Kind == CardKind.Character &&
                c.Info.Power <= 5000 &&
                c.Info.ColorList.Contains("黄") &&
                !string.IsNullOrEmpty(c.Info.Trigger)
            ).ToList();
            if (playable.Count == 0) return;

            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
                "登场最多 1 张力量≤5000 且拥有【触发】的黄色角色",
                playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = playable.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
            }
            return;
        }
    }
}
