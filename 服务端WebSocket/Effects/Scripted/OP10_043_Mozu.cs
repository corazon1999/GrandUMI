using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-043 牛牛（角色）
/// 【登场时】可以将我方1张拥有《德莱斯罗兹》特征的领袖或舞台转为休息状态：
///   本回合中，我方最多1张角色"蒙奇·D·路飞"获得【流放】效果。
///
/// 说明 / 简化点：
///   - 成本为"将我方 1 张《德莱斯罗兹》领袖或舞台转为休息状态"，用 RestCard 实现。
///   - 收益为给最多 1 张名为"蒙奇·D·路飞"的我方角色赋予本回合【流放】。
///     【流放】为本回合临时关键词，用 GiveKeyword(... ThisTurn) 实现。
/// </summary>
public class OP10_043_Mozu : IScriptedEffect
{
    public string CardNumber => "OP10-043";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 成本候选：我方拥有《德莱斯罗兹》特征且可休置的领袖或舞台
        var costTargets = new List<CardInstance>();
        if (me.Leader.Info.HasKeyword("德莱斯罗兹") && !me.Leader.IsTapped) costTargets.Add(me.Leader);
        if (me.StageCard is not null && me.StageCard.Info.HasKeyword("德莱斯罗兹") && !me.StageCard.IsTapped)
            costTargets.Add(me.StageCard);

        // 收益候选：我方名为"蒙奇·D·路飞"的角色
        var luffyChars = me.Characters.Where(c => c.MatchesName("蒙奇·D·路飞")).ToList();
        if (costTargets.Count == 0 || luffyChars.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "牛牛【登场时】：将 1 张《德莱斯罗兹》领袖或舞台转为休息状态，使 1 张路飞本回合获得【流放】？");
        if (!use) return;

        // 支付成本：选择并休置 1 张领袖/舞台
        var costPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrStage",
            "选择 1 张《德莱斯罗兹》领袖或舞台转为休息状态",
            costTargets.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (costPick.Count < 1) return;
        var costCard = costTargets.First(c => c.Id.ToString() == costPick[0]);
        AtomicOps.RestCard(costCard);

        // 收益：选择最多 1 张路飞获得本回合【流放】
        var benefit = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多 1 张角色\"蒙奇·D·路飞\"，使其本回合获得【流放】",
            luffyChars.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (benefit.Count > 0)
        {
            var tgt = luffyChars.First(c => c.Id.ToString() == benefit[0]);
            AtomicOps.GiveKeyword(tgt, "流放", KeywordDuration.ThisTurn);
        }
    }
}
