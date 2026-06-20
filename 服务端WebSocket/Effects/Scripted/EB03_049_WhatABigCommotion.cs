using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-049 果然是你们啊，弄出这么大的动静。（事件 / 地 / 恐怖之船海盗团 cost1）
/// 【主要】可以将我方的 7 张咚!! 转为休息状态：我方领袖为“佩罗娜”的场合，
///   可以将我方手牌或废弃区中拥有《恐怖之船海盗团》特征且费用不高于 6 的角色卡牌、
///   和拥有《恐怖之船海盗团》特征且费用不高于 4 的角色卡牌各最多 1 张登场。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 实现说明：
///   - 主要段(EventMain)：成本"将我方 7 张活跃咚转为休息状态"，"可以"=可选；活跃咚不足 7 则无法支付。
///     收益仅当领袖为"佩罗娜"时提供登场（否则只白付成本，故仅在该前提下询问发动）。
///   - 登场分两段：先选 1 张 cost≤6《恐怖之船海盗团》角色，再选 1 张 cost≤4《恐怖之船海盗团》角色，
///     候选合并手牌+废弃区，按所在区域分别 PlayFromHandFree / PlayFromTrashFree。
///   - 反击段(EventCounter)：我方领袖本次战斗 +3000。
/// </summary>
public class EB03_049_WhatABigCommotion : IScriptedEffect
{
    public string CardNumber => "EB03-049";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            // 【反击】本次战斗中，我方领袖力量 +3000
            AtomicOps.AddPowerThisBattle(me.Leader, 3000);
            return;
        }

        // 【主要】仅领袖为“佩罗娜”且活跃咚≥7 时有意义
        if (!me.Leader.Info.NameContains("佩罗娜")) return;
        if (me.ActiveDonCount < 7) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "果然是你们啊【主要】：将我方 7 张咚!! 转为休息状态，登场 cost≤6 与 cost≤4 的《恐怖之船海盗团》角色各最多 1 张？");
        if (!use) return;

        // 成本：将 7 张活跃咚转为休息状态
        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= 7) break;
            if (d.State == DonState.Active) { d.State = DonState.Rest; rested++; }
        }

        // 第一段：cost≤6《恐怖之船海盗团》角色，各来源最多 1 张
        await PlayOne(ctx, me, owner, 6, "登场最多 1 张 费用≤6 的《恐怖之船海盗团》角色");
        // 第二段：cost≤4《恐怖之船海盗团》角色
        await PlayOne(ctx, me, owner, 4, "登场最多 1 张 费用≤4 的《恐怖之船海盗团》角色");
    }

    private static async Task PlayOne(EffectContext ctx, PlayerState me, int owner, int maxCost, string text)
    {
        bool Eligible(CardInstance c) =>
            c.Info.Kind == CardKind.Character &&
            c.Info.HasKeyword("恐怖之船海盗团") &&
            c.Info.Cost <= maxCost;

        var cands = me.Hand.Where(Eligible).Concat(me.Trash.Where(Eligible)).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "HandOrTrashCharacter",
            text, cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = cands.First(c => c.Id.ToString() == chosen[0]);
        if (me.Hand.Contains(picked)) AtomicOps.PlayFromHandFree(ctx.State, owner, picked);
        else if (me.Trash.Contains(picked)) AtomicOps.PlayFromTrashFree(ctx.State, owner, picked);
    }
}
