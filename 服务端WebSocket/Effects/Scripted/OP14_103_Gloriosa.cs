using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-103 格罗丽欧莎（角色 / 光 2 费）
/// 【登场时】可以将我方生命区最上方或最下方的 1 张卡牌加入手牌：
///   将我方最多 1 张手牌加入生命区最上方。
///
/// 实现说明 / 简化点：
///   - 成本为"将生命区最上/最下方 1 张卡加入手牌"，需要生命区至少 1 张才能发动。
///   - 用 ChooseOption 让玩家选择取最上方还是最下方的生命卡（生命卡正面信息不公开，
///     仅给出位置选择，符合规则）。
///   - 收益为"将最多 1 张手牌加入生命区最上方"，加入后该卡在生命区，可不放（最多 1 张）。
/// </summary>
public class OP14_103_Gloriosa : IScriptedEffect
{
    public string CardNumber => "OP14-103";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 成本：生命区需至少 1 张
        if (me.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "格罗丽欧莎【登场时】：将生命区最上方或最下方 1 张卡加入手牌，并将最多 1 张手牌放入生命区最上方？");
        if (!use) return;

        // 成本：选择取最上方还是最下方的生命卡
        CardInstance lifeCard;
        if (me.LifeArea.Count == 1)
        {
            lifeCard = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
        }
        else
        {
            int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "选择将生命区哪一张加入手牌", new[] { "最上方", "最下方" });
            if (opt == 1)
            {
                lifeCard = me.LifeArea[me.LifeArea.Count - 1];
                me.LifeArea.RemoveAt(me.LifeArea.Count - 1);
            }
            else
            {
                lifeCard = me.LifeArea[0];
                me.LifeArea.RemoveAt(0);
            }
        }
        me.Hand.Add(lifeCard);

        // 收益：将最多 1 张手牌加入生命区最上方
        if (me.Hand.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "将最多 1 张手牌加入生命区最上方",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var picked = me.Hand.First(c => c.Id.ToString() == chosen[0]);
            me.Hand.Remove(picked);
            me.LifeArea.Insert(0, picked);
        }
    }
}
