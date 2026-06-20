using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-062 夏洛特·卡塔库栗（领航 5 费 5000，大妈海盗团）
/// 【攻击时】/【对方的攻击时】【每回合1次】咚!!-1：
///   确认对方卡组最上方的 1 张卡牌。之后，本次战斗中，此领袖的力量 +1000。
///
/// 实现说明 / 简化点：
///   - 触发节带成本(咚!!-1)且每回合1次，故用脚本表达。
///   - "确认对方卡组最上方 1 张"仅为本方信息，对游戏状态无影响（无公开/比对动作），
///     按惯例不做可见操作，直接给予 +1000 收益。
///   - 成本：咚!!-1（ReturnDonToDeck）；不足 1 张活跃咚时无法发动。
/// </summary>
public class OP11_062_CharlotteKatakuri : IScriptedEffect
{
    public string CardNumber => "OP11-062";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnAttackDeclare || t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 每回合1次
        var key = me.Leader.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本：咚!!-1，需有可放回的咚
        if (me.CostArea.Count < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "卡塔库栗：支付咚!!-1，确认对方卡组顶 1 张，本次战斗此领袖力量 +1000？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        me.TurnOnceUsed.Add(key);

        // 收益：本次战斗此领袖 +1000
        AtomicOps.AddPowerThisBattle(me.Leader, 1000);
    }
}
