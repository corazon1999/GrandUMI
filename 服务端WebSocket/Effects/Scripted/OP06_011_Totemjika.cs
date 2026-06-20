using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-011 托特姆吉卡（角色，炎 5 费 6000，FILM）
/// 【启动主要】【每回合1次】可以将我方的 1 张"乌塔"转为休息状态：
///   本回合中，此角色的力量 +5000。
///
/// 实现说明：
///   - 成本为"将我方 1 张名为'乌塔'的角色转为休息状态"（需为活跃状态才有意义）。
///     DSL 的 cost 节无"横置指定名称己方角色"键，故用脚本：从我方角色中筛出名含"乌塔"
///     且当前活跃的卡，玩家选 1 张横置作为成本。
///   - 可选效果（"可以"）先 ConfirmOptional；每回合 1 次用 TurnOnceUsed 标记。
/// </summary>
public class OP06_011_Totemjika : IScriptedEffect
{
    public string CardNumber => "OP06-011";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本候选：我方活跃状态、名含"乌塔"的角色
        var utaList = me.Characters.Where(c => c.MatchesName("乌塔") && !c.IsTapped).ToList();
        if (utaList.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "托特姆吉卡【启动主要】：将我方 1 张'乌塔'转为休息状态，本回合此角色力量 +5000？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张'乌塔'转为休息状态（成本）",
            utaList.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return; // 未支付成本

        var costCard = utaList.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.RestCard(costCard);

        me.TurnOnceUsed.Add(key);

        AtomicOps.AddPowerThisTurn(self, 5000);
    }
}
