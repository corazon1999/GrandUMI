using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-059 阿斯巴古（角色 / 暗 / 七水之城·卡雷拉公司）
/// 【对方攻击时】咚!!-1（可以将我方场上指定数量的咚!!放回咚!!卡组）：
///   我方领袖拥有《七水之城》特征的场合，本回合中，此角色获得【阻挡者】效果。
///
/// 实现说明：
///   - 仅领袖含《七水之城》时本效果有收益，否则不询问。
///   - 可选成本【咚!!-1】：ConfirmOptional 同意后 ReturnDonToDeck(me,1)；费用区需有可放回咚。
///   - 本回合此角色获得【阻挡者】：GiveKeyword(self,"阻挡者",ThisTurn)。
/// </summary>
public class OP04_059_Asparagus : IScriptedEffect
{
    public string CardNumber => "OP04-059";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (!me.Leader.Info.HasKeyword("七水之城")) return;
        if (me.TotalDonInCostArea < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "阿斯巴古【对方攻击时】：咚!!-1，本回合此角色获得【阻挡者】？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        AtomicOps.GiveKeyword(self, "阻挡者", KeywordDuration.ThisTurn);
    }
}
