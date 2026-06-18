using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-014 波尔萨利诺（角色）
/// 【阻挡者】（关键词，引擎处理）
/// 【启动主要】可以将此角色转为休息状态：本回合中，我方最多 1 张拥有《海军》特征的领袖或角色
///   也可以攻击对方处于活跃状态的角色。
///
/// 实现说明：
///   - 仅实现【启动主要】主动效果部分；【阻挡者】由引擎关键词处理。
///   - 成本：将此角色转为休息状态（已休息无法支付）。
///   - 收益：选择我方 1 张《海军》领袖或角色，赋予其本回合"可攻击活跃"关键词（Wave3），
///     ActionValidator 据此允许其攻击对方活跃状态的角色。
/// </summary>
public class OP11_014_Borsalino : IScriptedEffect
{
    public string CardNumber => "OP11-014";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本：将此角色转为休息状态（已休息无法支付）
        if (self.IsTapped) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "波尔萨利诺【启动主要】：将此角色转为休息状态，使我方 1 张《海军》领袖或角色本回合也可攻击对方活跃角色？");
        if (!use) return;

        // 候选：我方拥有《海军》特征的领袖或角色
        var targets = new List<CardInstance>();
        if (me.Leader.Info.HasKeyword("海军")) targets.Add(me.Leader);
        targets.AddRange(me.Characters.Where(c => c.Info.HasKeyword("海军")));
        if (targets.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择 1 张《海军》领袖或角色，使其本回合也可攻击对方活跃角色（最多 1 张）",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        // 支付成本：横置自身
        AtomicOps.RestCard(self);

        var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.GiveKeyword(tgt, "可攻击活跃", KeywordDuration.ThisTurn);
    }
}
