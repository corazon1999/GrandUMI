using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-012 蒙斯塔（角色）
/// 我方的角色"邦克·庞奇"因效果将要被KO的场合，可以改为将此角色放置到废弃区，使该角色不会被KO。
/// 实现说明：用 Wave5 的 OnAllyWillBeKOd 置换守护。仅覆盖经 KO 流程的被KO。
///   成本=将此角色（蒙斯塔自身）放置到废弃区；受益者须为"邦克·庞奇"且非自身。
/// </summary>
public class OP09_012_Monster : IScriptedEffect
{
    public string CardNumber => "OP09-012";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillBeKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        if (victim is null) return;
        if (victim.Id == self.Id) return;                    // 替身须为他卡
        if (!victim.Info.NameContains("邦克·庞奇")) return;   // 仅保护邦克·庞奇

        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "蒙斯塔：将自身放置到废弃区，使角色「邦克·庞奇」不被KO？")) return;

        // 成本：将此角色（自身）放置到废弃区
        AtomicOps.KO(ctx.State, ctx.OwnerIndex, self);
        // 置换：取消该角色被KO
        ctx.State.MarkPreventKO(victim.Id);
    }
}
