using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-021 橡皮橡皮机关枪（事件）
/// 【主要】赋予我方 1 张"蒙奇·D·路飞"最多 1 张休息状态的咚!!。
///         之后，本回合中，对方最多 1 张角色力量 -2000。
/// 【触发】本回合中，对方最多 1 张角色力量 -2000。
///
/// 实现说明 / 简化点：
///   - "赋予 1 张'蒙奇·D·路飞'休息咚" 需要在我方角色（含领袖）中筛选卡名为"蒙奇·D·路飞"者，
///     DSL 既有候选种类无法按卡名过滤，故用脚本实现。
///   - "赋予最多 1 张休息状态的咚!!"：从费用区取 1 张休息状态的咚附给目标（不足则尽力）。
///   - 两步均为"最多 1 张"，min=0 可跳过。
/// </summary>
public class OP13_021_GomuGatling : IScriptedEffect
{
    public string CardNumber => "OP13-021";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            await AttachDonToLuffy(ctx);
            await MinusPower(ctx);
        }
        else if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            await MinusPower(ctx);
        }
    }

    /// <summary>赋予我方 1 张"蒙奇·D·路飞"（领袖或角色）最多 1 张休息状态的咚!!</summary>
    static async Task AttachDonToLuffy(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 我方领袖 + 角色中卡名为"蒙奇·D·路飞"者
        var luffies = new List<CardInstance>();
        if (me.Leader.Info.NameIs("蒙奇·D·路飞")) luffies.Add(me.Leader);
        luffies.AddRange(me.Characters.Where(c => c.Info.Name == "蒙奇·D·路飞"));
        if (luffies.Count == 0) return;

        // 需要场上存在休息状态的咚才有意义
        bool hasRestDon = me.CostArea.Any(d => d.State == DonState.Rest);
        if (!hasRestDon) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择 1 张\"蒙奇·D·路飞\"，赋予最多 1 张休息状态的咚!!",
            luffies.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = luffies.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Rest);
    }

    /// <summary>本回合中，对方最多 1 张角色力量 -2000</summary>
    static async Task MinusPower(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var candidates = opp.Characters.ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张角色，本回合力量 -2000",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddPowerThisTurn(target, -2000);
    }
}
