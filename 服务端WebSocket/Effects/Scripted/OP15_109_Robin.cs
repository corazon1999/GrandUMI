using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-109 妮古·罗宾（角色 / 光 7 费 7000，空岛/草帽一伙）
/// 【登场时】可以将我方生命区最上方的 1 张卡牌加入手牌：
///   我方领袖拥有《草帽一伙》特征的场合，将我方卡组最上方的最多 1 张卡牌加入生命区最上方。
///   之后，将我方手牌中最多 1 张费用不高于 5 且拥有《空岛》特征的角色卡牌登场。
///
/// 实现说明 / 简化点：
///   - 成本"将生命区最上方 1 张加入手牌"为可选发动开关("可以…")，无生命牌则无法发动。
///   - 收益 1 仅在领袖含《草帽一伙》时执行(卡组顶 1 张入生命顶)，"最多 1 张"实现为可选确认。
///   - 收益 2 从手牌登场最多 1 张费用≤5 且《空岛》角色(min=0 可不选)。
/// </summary>
public class OP15_109_Robin : IScriptedEffect
{
    public string CardNumber => "OP15-109";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (me.LifeArea.Count == 0) return; // 无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "罗宾【登场时】：将生命区最上方 1 张加入手牌并发动效果？");
        if (!use) return;

        // 成本：生命区最上方 1 张加入手牌
        var top = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        me.Hand.Add(top);

        // 收益 1：领袖含《草帽一伙》时，卡组顶最多 1 张加入生命区最上方
        if (me.Leader.Info.HasKeyword("草帽一伙") && me.Deck.Count > 0)
        {
            bool addLife = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将卡组最上方 1 张加入生命区最上方？");
            if (addLife) AtomicOps.AddLifeFromDeckTop(me, 1);
        }

        // 收益 2：手牌中最多 1 张费用≤5 且《空岛》角色登场
        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 5 &&
            c.Info.HasKeyword("空岛")
        ).ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场我方手牌中最多 1 张费用≤5 且《空岛》角色（可不选）",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = playable.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
    }
}
