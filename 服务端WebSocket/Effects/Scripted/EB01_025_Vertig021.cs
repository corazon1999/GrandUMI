using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-025 弗特里克斯（角色 / 水 / 百兽海盗团・SMILE，cost3 power5000）
/// 【触发】可以将我方生命区最上方或最下方的 1 张卡牌加入手牌：
///   将我方的最多 1 张手牌加入生命区最上方。
///
/// 实现说明（生命触发，OnLifeRevealTrigger）：
///   - 触发时此卡已在废弃区（生命牌发动触发后进废弃）。整段为可选（"可以"）。
///   - 发动则：选生命区最上方或最下方 1 张入手；之后选≤1 张手牌置入生命区最上方。
/// </summary>
public class EB01_025_Vertig021 : IScriptedEffect
{
    public string CardNumber => "EB01-025";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "弗特里克斯【触发】：将生命区最上方或最下方 1 张加入手牌，再将最多 1 张手牌置入生命区最上方？");
        if (!use) return;

        // 选生命区最上方或最下方 1 张入手
        CardInstance lifeCard;
        if (me.LifeArea.Count == 1)
        {
            lifeCard = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
        }
        else
        {
            int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "将生命区哪一张加入手牌？", new[] { "最上方", "最下方" });
            int idx = where == 1 ? me.LifeArea.Count - 1 : 0;
            lifeCard = me.LifeArea[idx];
            me.LifeArea.RemoveAt(idx);
        }
        me.Hand.Add(lifeCard);

        // 之后：将最多 1 张手牌置入生命区最上方
        if (me.Hand.Count == 0) return;
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "选择最多 1 张手牌置入生命区最上方",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;
        var handCard = me.Hand.First(c => c.Id.ToString() == chosen[0]);
        me.Hand.Remove(handCard);
        me.LifeArea.Insert(0, handCard);
    }
}
