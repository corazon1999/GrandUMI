using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-105 山智（角色 / 光 5 费 6000，艾格赫德/草帽一伙）
///
/// 完整文本：
///   （本体无效果）
///   【触发】我方领袖拥有《艾格赫德》特征的场合，将我方卡组最上方的最多 1 张卡牌
///   加入生命区最上方。之后，丢弃我方的 2 张手牌。
///
/// 实现：
///   - 生命牌【触发】(OnLifeRevealTrigger)：仅当我方领袖含《艾格赫德》时发动。
///   - 满足条件：卡组顶 1 张入生命区最上方（AddLifeFromDeckTop）；之后让玩家选 2 张手牌丢弃。
///
/// 简化点：trigger 节 DSL 不支持 if 条件，故用脚本判定领袖特征。
/// </summary>
public class OP09_105_Sanji : IScriptedEffect
{
    public string CardNumber => "OP09-105";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        // 条件：我方领袖拥有《艾格赫德》特征
        if (!me.Leader.Info.HasKeyword("艾格赫德")) return;

        // 将卡组最上方最多 1 张加入生命区最上方
        AtomicOps.AddLifeFromDeckTop(me, 1);

        // 之后：丢弃我方 2 张手牌
        int n = Math.Min(2, me.Hand.Count);
        if (n == 0) return;
        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方的 2 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), n, n, discardExtra);
        foreach (var id in chosen)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == id);
            if (card != null) AtomicOps.DiscardHand(me, card);
        }
    }
}
