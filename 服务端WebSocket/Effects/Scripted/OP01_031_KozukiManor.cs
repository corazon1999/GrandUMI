using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-031 光月御殿（领航）
/// 【启动主要】【每回合1次】可以丢弃我方手牌中1张拥有《和之国》特征的卡牌：
///   将我方最多2张咚!!转为活跃状态。
///
/// 实现说明：
///   - 可选成本：丢弃手牌中 1 张含《和之国》特征(关键词)的卡牌；用 ChooseCards 限定候选为该特征牌。
///   - 收益：将我方最多 2 张休息状态的咚!!转为活跃（与 OP02-026 同法）。
///   - 每回合1次：TurnOnceUsed。
/// </summary>
public class OP01_031_KozukiManor : IScriptedEffect
{
    public string CardNumber => "OP01-031";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本候选：手牌中含《和之国》特征的卡牌
        var wano = me.Hand.Where(c => c.Info.HasKeyword("和之国")).ToList();
        if (wano.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "光月御殿【启动主要】：丢弃1张《和之国》特征手牌，将我方最多2张咚!!转为活跃？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = wano.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃1张《和之国》特征的手牌（成本）",
            wano.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (disc.Count == 0) return; // 未支付成本

        me.TurnOnceUsed.Add(key);

        var discarded = wano.First(c => c.Id.ToString() == disc[0]);
        AtomicOps.DiscardHand(me, discarded);

        // 收益：最多2张休息咚转为活跃
        int n = 0;
        foreach (var d in me.CostArea)
        {
            if (n >= 2) break;
            if (d.State == DonState.Rest) { d.State = DonState.Active; n++; }
        }
    }
}
