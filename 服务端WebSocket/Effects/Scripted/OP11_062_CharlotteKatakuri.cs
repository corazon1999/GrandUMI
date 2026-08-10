using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-062 夏洛特·卡塔库栗（领航 5 费 5000，大妈海盗团）
/// 【攻击时】/【对方的攻击时】【每回合1次】咚!!-1：
///   确认对方卡组最上方的 1 张卡牌。之后，本次战斗中，此领袖的力量 +1000。
///
/// 实现说明：
///   - 触发节带成本(咚!!-1)且每回合1次，故用脚本表达。
///   - 支付成本后，通过私密选择提示仅向发动方展示对方卡组最上方 1 张，不改变牌组状态与顺序。
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

        // 私下确认对方牌组顶 1 张：仅发动方可见，不移动卡牌或改变牌组顺序。
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        if (opp.Deck.Count > 0)
        {
            var top = opp.Deck[0];
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = new[] { new { id = top.Id.ToString(), number = top.Info.Number } },
            };
            await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookOppTop",
                "确认对方卡组最上方的 1 张卡牌", new List<string>(), 0, 0, extra);
        }

        // 收益：本次战斗此领袖 +1000
        AtomicOps.AddPowerThisBattle(me.Leader, 1000);
    }
}
