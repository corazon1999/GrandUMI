using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-027 耕四郎（角色 / 绿 / 2费）
/// 我方此角色以外的费用≤5且拥有属性〈斩〉的角色因对方的效果将要被KO的场合，
///   可以改为将此角色转为休息状态，使该角色不会被KO。【阻挡者】
/// 实现：OnAllyWillBeKOd 守护（仅"因对方效果"，参照 EB04-043 判 KOReason/KOActingSide）；
///   成本=横置自身；MarkPreventKO 取消该次KO。
///   【阻挡者】为卡面常驻能力（数据 abilities），引擎自动识别，脚本不处理。
/// </summary>
public class OP12_027_Koshiro : IScriptedEffect
{
    public string CardNumber => "OP12-027";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillBeKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 仅"因对方的效果"将要被KO
        if (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex) return;

        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        if (victimId is null || victimId == self.Id.ToString()) return;   // 此角色以外
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        if (victim is null) return;
        if (victim.Info.Cost > 5) return;             // 费用≤5
        if (victim.Info.Property != "斩") return;      // 属性〈斩〉
        if (self.IsTapped) return;                     // 成本：自身需活跃

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"耕四郎：将此角色转为休息状态，使「{victim.Info.Name}」不会被KO？");
        if (!use) return;

        AtomicOps.RestCard(self);
        ctx.State.MarkPreventKO(victim.Id);
    }
}
