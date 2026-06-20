using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-071 Mr.4(贝布)（角色 / 暗 / 巴洛克工作室）
/// 【对方攻击时】咚!!-1（可以将我方场上指定数量的咚!!放回咚!!卡组）：
///   本回合中，此角色获得【阻挡者】效果，力量+1000。
///
/// 实现说明：
///   - 可选成本咚-1；之后本回合此角色获得【阻挡者】并 +1000。
///   - "将攻击对象改为此卡牌"是【阻挡者】关键词的常规结算，由引擎处理，故仅赋予关键词即可。
/// </summary>
public class OP04_071_MrFour : IScriptedEffect
{
    public string CardNumber => "OP04-071";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (me.TotalDonInCostArea < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "Mr.4【对方攻击时】：咚!!-1，本回合此角色获得【阻挡者】并 +1000？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        AtomicOps.GiveKeyword(self, "阻挡者", KeywordDuration.ThisTurn);
        AtomicOps.AddPowerThisTurn(self, 1000);
    }
}
