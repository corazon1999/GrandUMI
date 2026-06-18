using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-111 小边（角色）
/// 【登场时】可以从我方的手牌中登场 1 张"小鸟"：
///   将对方最多 1 张费用不高于 3 的角色正面朝上加入其生命区的最上方或最下方。
///
/// 说明 / 简化点：
///   - "可以从手牌登场小鸟"为发动本效果的可选成本：仅当手牌中存在"小鸟"时提示，登场后才结算收益。
///   - "加入其(对方)生命区最上方或最下方"用 MoveCharToLife(s, 对方下标, card, toTop)，
///     toTop 经 ChooseOption("最上方"/"最下方") 决定。该 op 会把目标从对方场上移入对方生命区。
/// </summary>
public class OP05_111_Hotoji : IScriptedEffect
{
    public string CardNumber => "OP05-111";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本：可以从手牌登场 1 张"小鸟"
        var birds = me.Hand.Where(c => c.MatchesName("小鸟") && c.Info.Kind == CardKind.Character).ToList();
        if (birds.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "小边【登场时】：从手牌登场 1 张\"小鸟\"，将对方 1 张费用≤3 的角色加入其生命区？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = birds.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pickBird = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "从手牌登场 1 张\"小鸟\"", birds.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (pickBird.Count == 0) return; // 未支付成本
        var bird = birds.First(c => c.Id.ToString() == pickBird[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, bird);

        // 收益：将对方最多 1 张费用≤3 的角色加入其生命区最上方或最下方
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 3).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤3 的角色加入其生命区",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;
        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);

        int pos = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "加入对方生命区的位置", new[] { "最上方", "最下方" });
        AtomicOps.MoveCharToLife(ctx.State, 1 - ctx.OwnerIndex, tgt, toTop: pos == 0);
    }
}
