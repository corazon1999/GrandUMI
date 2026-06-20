using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-060 蒙奇·D·路飞（领航）
/// 【启动主要】【每回合1次】可以将我方生命区最上方的 1 张卡牌加入手牌：
///   我方场上不存在咚!!，或我方场上存在 3 张或更多咚!! 的场合，从咚!!卡组中追加最多 1 张活跃状态的咚!!。
///
/// 实现说明：
///   - 启动条件为"场上无咚 或 场上≥3咚"的或(OR)关系，用 C# || 直接表达。
///   - 成本为"生命区最上方 1 张加入手牌"，无可选语义但整体效果为"可以"，故 ConfirmOptional 询问。
///   - 每回合1次：用 TurnOnceUsed 防重复。
///   - 追加咚受咚卡组余量/费用区上限约束（RefreshDonFromDeck 内部处理）。
/// </summary>
public class OP05_060_Luffy : IScriptedEffect
{
    public string CardNumber => "OP05-060";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        if (me.LifeArea.Count == 0) return; // 无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞【启动主要】：将生命区最上方 1 张加入手牌，并按条件追加最多 1 张活跃咚!!？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);

        // 成本：生命区最上方 1 张加入手牌
        var top = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        me.Hand.Add(top);

        // 收益：场上无咚 或 场上≥3咚 → 追加最多 1 张活跃咚
        int donOnField = me.TotalDonInCostArea;
        if (donOnField == 0 || donOnField >= 3)
        {
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        }
    }
}
