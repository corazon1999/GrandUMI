using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-110 夏洛特·果昔（角色）
/// 【攻击时】可以将我方生命区最上方或最下方的 1 张卡牌加入手牌：本次战斗中，此角色的力量+2000。
///
/// 说明 / 简化点：
///   - 可选成本"生命顶或底 1 张入手"：用 ChooseOption 让玩家选顶/底/不发动。
///   - 支付成本后本次战斗 +2000（AddPowerThisBattle）。
///   - 【触发】丢弃手牌登场为触发关键词，引擎单独处理。
/// </summary>
public class OP03_110_Smoothie : IScriptedEffect
{
    public string CardNumber => "OP03-110";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (me.LifeArea.Count == 0) return; // 无生命牌无法支付成本

        var options = new List<string> { "生命区最上方", "生命区最下方", "不发动" };
        int pick = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "果昔【攻击时】：将我方生命区最上方或最下方 1 张加入手牌，本次战斗力量+2000？", options);
        if (pick == 2) return;

        var card = pick == 0 ? me.LifeArea[0] : me.LifeArea[me.LifeArea.Count - 1];
        me.LifeArea.Remove(card);
        me.Hand.Add(card);

        AtomicOps.AddPowerThisBattle(self, 2000);
    }
}
