using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-100 萨波（5 费 6000 黄色）
/// 我方生命卡牌不多于 3 张的场合，此角色获得【阻挡者】效果，费用 +3。
/// 【登场时】可以将我方生命区最上方的 1 张卡牌加入手牌：抽取 2 张卡牌，丢弃我方的 1 张手牌。
///
/// 实现范围：本脚本实现【登场时】可选效果——
///   成本：将我方生命区最上方的 1 张卡牌加入手牌（需生命区非空）。
///   效果：抽取 2 张卡牌；之后丢弃我方 1 张手牌（玩家自选，经 extra.choiceCards 显示卡面）。
///
/// 简化点（未实现）：
///   - 前半"生命≤3 时获得【阻挡者】、费用+3"为持续型静态效果（被动、随生命数量动态生效），
///     引擎目前没有可在脚本里挂载的持续静态状态通道，此处未实现。该被动需引擎层支持。
/// </summary>
public class OP12_100_Sabo : IScriptedEffect
{
    public string CardNumber => "OP12-100";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 成本：需生命区有牌才能将最上方 1 张加入手牌
        if (me.LifeArea.Count == 0) return;

        // 可选效果：询问是否发动
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "萨波【登场时】：将生命区最上方 1 张加入手牌，然后抽 2 张、丢弃 1 张手牌？");
        if (!use) return;

        // 支付成本：生命区最上方 1 张加入手牌
        var top = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        me.Hand.Add(top);

        // 效果：抽取 2 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

        // 之后：丢弃我方 1 张手牌（玩家自选）
        if (me.Hand.Count == 0) return;
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "SaboDiscard",
            "丢弃 1 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (chosen.Count > 0)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
            if (card is not null) AtomicOps.DiscardHand(me, card);
        }
        else
        {
            // 超时未选 → 自动丢第一张
            AtomicOps.DiscardHand(me, me.Hand[0]);
        }
    }
}
