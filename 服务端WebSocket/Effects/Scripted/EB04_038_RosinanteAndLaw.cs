using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-038 罗西南德&罗（角色 / 暗 / 海军・堂吉诃德海盗团 / cost6 power8000）
/// 规则上此卡名也视为"特拉法尔加·罗"和"堂吉诃德·罗西南德"（名称规则，与本脚本效果无关，省略）。
/// 【阻挡者】（关键词，由引擎处理）
/// 【登场时】我方场上咚!!的张数不多于对方场上咚!!的张数的场合，抽取1张卡牌。
///   之后，从咚!!卡组中追加最多1张活跃状态的咚!!。
///
/// 实现说明：
///   - 条件：我方费用区咚!!总数 ≤ 对方费用区咚!!总数（TotalDonInCostArea）。
///   - 满足则抽 1 张，再 RefreshDonFromDeck 1 活跃咚。
///   - 【阻挡者】为关键词，引擎处理，本脚本只实现【登场时】。
/// </summary>
public class EB04_038_RosinanteAndLaw : IScriptedEffect
{
    public string CardNumber => "EB04-038";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方咚!!张数 ≤ 对方咚!!张数
        if (me.TotalDonInCostArea > opp.TotalDonInCostArea) return Task.CompletedTask;

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);

        return Task.CompletedTask;
    }
}
