using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-102 山智（角色）
/// 【咚!!×2】【攻击时】可以将我方生命区最上方或最下方的 1 张卡牌加入手牌：
///   将我方卡组最上方的最多 1 张卡牌加入生命区最上方。
///
/// 说明 / 简化点：
///   - 【咚!!×2】发动条件由引擎校验，脚本只实现收益（可选成本 + 结果）。
///   - 成本"生命顶或底 1 张入手"为可选发动门槛：用 ChooseOption 让玩家选顶/底/不发动。
///   - 结果：卡组顶 1 张加入生命区最上方（AddLifeFromDeckTop）。
/// </summary>
public class OP03_102_Sanji : IScriptedEffect
{
    public string CardNumber => "OP03-102";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (me.LifeArea.Count == 0) return; // 无生命牌则无法支付成本

        var options = new List<string> { "生命区最上方", "生命区最下方", "不发动" };
        int pick = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "山智【攻击时】：将我方生命区最上方或最下方 1 张加入手牌（之后卡组顶 1 张入生命）？", options);
        if (pick == 2) return; // 不发动

        // 支付成本：生命顶或底 1 张入手
        var card = pick == 0 ? me.LifeArea[0] : me.LifeArea[me.LifeArea.Count - 1];
        me.LifeArea.Remove(card);
        me.Hand.Add(card);

        // 结果：卡组顶最多 1 张加入生命区最上方
        AtomicOps.AddLifeFromDeckTop(me, 1);
    }
}
