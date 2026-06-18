using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-008 安普里奥·伊万科夫（角色 / 炎 / 革命军，cost2 power3000）
/// 我方拥有《革命军》特征的角色因对方的效果将要被KO的场合，
///   可以改为将此角色（自身）放置到废弃区，使该角色不会被KO。
///
/// 实现说明：
///   - 用 OnAllyWillBeKOd 守护者钩子（战斗KO时派发）；效果KO时引擎设 KOReason="effect"、
///     KOActingSide=发起方，据此判定"因对方的效果"。
///   - 被守护目标须拥有《革命军》特征。置换成本：将自身放置废弃区（含归还附着咚），MarkPreventKO 取消KO。
///   - 局限：仅战斗/效果KO派发；脚本直接同步KO的极端场景可能不覆盖（已文档化）。
/// </summary>
public class OP13_008_Ivankov : IScriptedEffect
{
    public string CardNumber => "OP13-008";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillBeKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 仅"因对方的效果"
        if (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex) return;

        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        if (victimId is null) return;
        if (victimId == self.Id.ToString()) return; // 不能替自己
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        if (victim is null) return;

        // 被守护目标须拥有《革命军》特征
        if (!victim.Info.HasKeyword("革命军")) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "伊万科夫：将此角色放置到废弃区，使该《革命军》角色不会被KO？");
        if (!use) return;

        // 置换成本：将自身放置废弃区（归还附着咚 + 移入废弃区）
        foreach (var d in me.CostArea)
        {
            if (d.State == DonState.Attached && d.AttachedToCardId == self.Id)
            {
                d.State = DonState.Rest;
                d.AttachedToCardId = null;
            }
        }
        me.Characters.Remove(self);
        me.Trash.Add(self);

        ctx.State.MarkPreventKO(victim.Id);
    }
}
