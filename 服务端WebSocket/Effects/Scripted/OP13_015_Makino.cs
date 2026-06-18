using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-015 卷乃（1 费 0，风车村）
/// 【启动主要】可以将此角色转为休息状态：
///   本回合中，我方最多 1 张"蒙奇·D·路飞"力量 +2000。
///
/// 实现：成本为把自身转休息（自身已休息则无法发动）。
/// 之后让玩家从我方场上名为"蒙奇·D·路飞"的角色中选最多 1 张，本回合力量 +2000。
/// </summary>
public class OP13_015_Makino : IScriptedEffect
{
    public string CardNumber => "OP13-015";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本：将此角色转为休息状态（已休息则无法支付）
        if (self.IsTapped) return;
        self.IsTapped = true;

        // 效果：我方最多 1 张"蒙奇·D·路飞"力量 +2000
        var candidates = me.Characters
            .Where(c => c.MatchesName("蒙奇·D·路飞"))
            .ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方最多 1 张\"蒙奇·D·路飞\"，本回合力量 +2000",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddPowerThisTurn(target, 2000);
    }
}
