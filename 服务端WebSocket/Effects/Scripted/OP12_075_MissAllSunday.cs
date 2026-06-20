using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-075 Miss.全周日（暗 / 巴洛克工作室）
/// 【登场时】将对方最多 1 张费用不高于 3 的角色 KO。之后，对方可以从咚!!卡组中追加 1 张活跃状态的咚!!。
/// 【触发】咚!!-1：此卡牌登场。
///
/// 说明 / 简化点：
///   - 登场时：先让我方选最多 1 张对方"当前费用 ≤3"的角色 KO（可选 0~1 张）。
///     之后由对方决定是否从其咚卡组追加 1 张活跃咚（对方的可选收益）。
///   - 触发：生命牌触发时，此卡已被 LifeRevealManager 移入废弃区。
///     成本"咚!!-1"= 把我方 1 张咚放回咚卡组（活跃不足时也允许放回休息咚）；
///     支付成功后从废弃区将此卡免费登场。无可放回的咚则无法支付，不登场。
/// </summary>
public class OP12_075_MissAllSunday : IScriptedEffect
{
    public string CardNumber => "OP12-075";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField)
            await ResolveEnter(ctx);
        else if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
            await ResolveTrigger(ctx);
    }

    // 【登场时】KO 对方 ≤3 角色；之后对方可追加 1 张活跃咚
    private static async Task ResolveEnter(EffectContext ctx)
    {
        var s = ctx.State;
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = s.Players[oppIdx];

        // 1. 将对方最多 1 张费用不高于 3 的角色 KO（可选）
        var candidates = opp.Characters.Where(c => c.Info.Cost <= 3).ToList();
        if (candidates.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张费用不高于 3 的角色 KO",
                candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var picked = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
                if (picked is not null) AtomicOps.KO(s, oppIdx, picked);
            }
        }

        // 2. 之后，对方可以从咚卡组追加 1 张活跃咚（由对方决定）
        if (opp.DonDeck.Count > 0 && opp.CostArea.Count < 10)
        {
            bool add = await ctx.Prompts.ConfirmOptional(oppIdx,
                "Miss.全周日【登场时】：你（对方）可以从咚卡组追加 1 张活跃状态的咚!!，是否追加？");
            if (add) AtomicOps.RefreshDonFromDeck(opp, 1, DonState.Active);
        }
    }

    // 【触发】咚!!-1：此卡牌登场（此时卡已在废弃区）
    private static async Task ResolveTrigger(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        // 可选触发：询问是否发动
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "Miss.全周日【触发】：咚!!-1，将此卡牌登场？");
        if (!use) return;

        // 成本：咚!!-1（把 1 张咚放回咚卡组）
        if (me.CostArea.Count < 1) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        // 从废弃区将此卡免费登场
        if (me.Trash.Contains(ctx.Source))
            AtomicOps.PlayFromTrashFree(s, ctx.OwnerIndex, ctx.Source, restState: false);
    }
}
