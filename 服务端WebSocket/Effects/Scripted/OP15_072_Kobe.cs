using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-072 小边（1 费 2000，空岛）
/// 【启动主要】咚!!-2，可以将此角色转为休息状态：
///   我方场上存在“小鸟”和“阿悟”的场合，本回合中，对方最多 1 张角色力量-3000。
///
/// 实现说明 / 简化点：
/// - 成本：咚!!-2（ReturnDonToDeck）+ 将此角色转为休息状态（RestCard，须自身活跃）。
/// - 条件"我方场上存在『小鸟』和『阿悟』"用 me.Characters/Leader 按名称匹配判定（C# 可表达，
///   DSL 的 if 条件节无按名称在场判定键，故此处用脚本）。
/// - 收益：选择对方最多 1 张角色，本回合 -3000（AddPowerThisTurn 负值）。
/// </summary>
public class OP15_072_Kobe : IScriptedEffect
{
    public string CardNumber => "OP15-072";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;
        int oppIdx = 1 - ctx.OwnerIndex;

        // 成本前置条件：自身活跃 + 费用区至少 2 张可返还的咚（活跃/休息/附着皆可）
        if (self.IsTapped) return;
        if (me.CostArea.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "小边【启动主要】：支付咚!!-2 并将此角色转为休息状态以发动效果？");
        if (!use) return;

        // 支付成本
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 2)) return;
        AtomicOps.RestCard(self);

        // 条件：我方场上存在“小鸟”和“阿悟”
        bool hasKotori = me.Characters.Any(c => c.MatchesName("小鸟"));
        bool hasAgo = me.Characters.Any(c => c.MatchesName("阿悟"));
        if (!hasKotori || !hasAgo) return;

        // 收益：对方最多 1 张角色本回合 -3000
        var cands = opp.Characters.ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张角色，本回合力量-3000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, -3000);
        }
    }
}
