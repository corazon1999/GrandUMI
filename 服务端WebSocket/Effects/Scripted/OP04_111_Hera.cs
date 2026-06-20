using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-111 赫拉（角色 / 光 / 大妈海盗团・Homies）
/// 【启动主要】可以将此角色以外我方 1 张拥有《Homies》特征的角色放置到废弃区，
///   将此角色转为休息状态：将我方最多 1 张角色"夏洛特·玲玲"转为活跃状态。
///
/// 实现说明：
///   - 成本：弃掉此角色以外 1 张《Homies》角色 + 横置自身（须二者皆能支付才发动）。
///   - 收益：将我方 1 张名为"夏洛特·玲玲"的角色转为活跃。
///   - "可以"为可选效果，先 ConfirmOptional。
/// </summary>
public class OP04_111_Hera : IScriptedEffect
{
    public string CardNumber => "OP04-111";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本候选：此角色以外的我方《Homies》角色
        var costCands = me.Characters
            .Where(c => c.Id != self.Id && c.Info.HasKeyword("Homies"))
            .ToList();
        if (costCands.Count == 0) return;

        // 收益候选：我方名为"夏洛特·玲玲"的角色
        var benefitCands = me.Characters.Where(c => c.MatchesName("夏洛特·玲玲")).ToList();
        if (benefitCands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "赫拉【启动主要】：废弃 1 张其他《Homies》角色并横置自身，将 1 张「夏洛特·玲玲」转为活跃？");
        if (!use) return;

        // 成本 1：选 1 张《Homies》角色放置到废弃区
        var costExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = costCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var costPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将此角色以外 1 张《Homies》角色放置到废弃区",
            costCands.Select(c => c.Id.ToString()).ToList(), 1, 1, costExtra);
        if (costPick.Count == 0) return;
        var sacrifice = costCands.First(c => c.Id.ToString() == costPick[0]);
        me.Characters.Remove(sacrifice);
        me.Trash.Add(sacrifice);

        // 成本 2：横置自身
        AtomicOps.RestCard(self);

        // 收益：将我方最多 1 张"夏洛特·玲玲"转为活跃
        var benefit = me.Characters.Where(c => c.MatchesName("夏洛特·玲玲")).ToList();
        if (benefit.Count == 0) return;
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方最多 1 张角色「夏洛特·玲玲」转为活跃",
            benefit.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var tgt = benefit.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.ActivateCard(tgt);
        }
    }
}
