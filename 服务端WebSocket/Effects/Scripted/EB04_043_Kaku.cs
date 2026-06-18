using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-043 卡古（角色 / 地 / 艾格赫德・CP0，cost3 power4000）
/// 【每回合1次】我方原本费用≤5 的黑色角色因对方的效果将要被KO的场合，
///   可以改为将我方废弃区中的 3 张卡牌自选顺序放回卡组最下方，使该角色不会被KO。
/// 【登场时】将我方卡组最上方的 2 张卡牌放置到废弃区。
///
/// 实现说明（M6 效果KO置换守护）：
///   - 守护用 OnAllyWillBeKOd；本次为效果KO时引擎已设 s.KOReason="effect"、s.KOActingSide=发起方，
///     据此判定"因对方的效果"（KOReason=="effect" 且发起方为对方）。
///   - 置换成本：废弃区 3 张自选顺序放回卡组最下方（ReturnTrashToDeckBottom）；MarkPreventKO 取消KO。
///   - 局限：脚本直接同步KO（非DSL）不派发守护，已文档化。
/// </summary>
public class EB04_043_Kaku : IScriptedEffect
{
    public string CardNumber => "EB04-043";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAllyWillBeKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            AtomicOps.MillTop(me, 2);
            return;
        }

        // OnAllyWillBeKOd：守护
        var key = ctx.Source.Info.Number + "-guard" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 仅"因对方的效果"
        if (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex) return;

        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        if (victimId is null) return;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        if (victim is null) return;

        // 我方原本费用≤5 的黑色（暗）角色
        if (victim.Info.Cost > 5) return;
        if (!victim.Info.ColorList.Contains("紫")) return;

        // 置换成本：废弃区需有 3 张
        if (me.Trash.Count < 3) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "卡古【每回合1次】：将废弃区 3 张自选顺序放回卡组最下方，使该角色不会被KO？");
        if (!use) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrash",
            "选择废弃区 3 张卡牌，按自选顺序放回卡组最下方",
            me.Trash.Select(c => c.Id.ToString()).ToList(), 3, 3);
        if (pick.Count < 3) return;
        foreach (var id in pick)
        {
            var card = me.Trash.FirstOrDefault(c => c.Id.ToString() == id);
            if (card is not null) AtomicOps.ReturnTrashToDeckBottom(me, card);
        }

        ctx.State.MarkPreventKO(victim.Id);
        me.TurnOnceUsed.Add(key);
    }
}
