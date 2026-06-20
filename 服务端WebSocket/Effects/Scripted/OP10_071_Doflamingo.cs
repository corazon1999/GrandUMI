using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-071 堂吉诃德·多弗拉门戈（角色 / 王下七武海 / 堂吉诃德海盗团）
/// 【登场时】咚!!-1：将我方手牌中最多 1 张费用不高于 5 且拥有《堂吉诃德海盗团》特征的角色卡牌登场。
/// 【对方的攻击时】【每回合1次】可以将我方的 1 张咚!!转为休息状态：从咚!!卡组中追加最多 1 张活跃状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 登场时成本"咚!!-1"为强制成本；触发节无法表达可选支付，按惯例：有登场目标时支付并登场，否则不发动。
///   - "费用不高于 5"取卡面原始费用 c.Info.Cost。
///   - 对方攻击时为可选（"可以"），成本 1 张活跃咚转休息，收益从咚卡组追加 1 张活跃咚。
/// </summary>
public class OP10_071_Doflamingo : IScriptedEffect
{
    public string CardNumber => "OP10-071";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 成本：咚!!-1，需要场上有可放回的咚
            int returnable = me.CostArea.Count;
            if (returnable < 1) return;

            var playable = me.Hand.Where(c =>
                c.Info.Kind == CardKind.Character &&
                c.Info.Cost <= 5 &&
                c.Info.HasKeyword("堂吉诃德海盗团")).ToList();
            if (playable.Count == 0) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "多弗拉门戈【登场时】：咚!!-1，登场 1 张费用≤5 的《堂吉诃德海盗团》角色？");
            if (!use) return;

            if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
                "登场最多 1 张费用≤5 的《堂吉诃德海盗团》角色",
                playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = playable.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
            }
            return;
        }

        // 【对方的攻击时】【每回合1次】
        var key = self.Info.Number + "-oppatk" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        int activeDon = me.CostArea.Count(d => d.State == DonState.Active);
        if (activeDon < 1) return;

        bool use2 = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "多弗拉门戈【对方的攻击时】：将我方 1 张咚!!转为休息状态，从咚!!卡组追加 1 张活跃咚？");
        if (!use2) return;

        foreach (var d in me.CostArea)
        {
            if (d.State == DonState.Active) { d.State = DonState.Rest; break; }
        }

        me.TurnOnceUsed.Add(key);

        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
    }
}
