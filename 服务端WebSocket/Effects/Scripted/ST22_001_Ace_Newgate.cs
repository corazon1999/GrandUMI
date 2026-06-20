using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST22-001 艾斯&纽哥特（领航/领袖）
/// 【启动主要】【每回合1次】可以公开我方手牌中 1 张拥有的特征中包含〈白胡子海盗团〉的卡牌：
///   抽取 1 张卡牌，并将公开的卡牌放回卡组最上方。
///
/// 实现说明 / 简化点：
///   - "公开"为发动成本：从手牌中选 1 张含〈白胡子海盗团〉特征的卡，向对手广播公开（仍留手牌）。
///   - 之后抽 1 张，再将那张公开的卡从手牌移到卡组最上方。
///   - 每回合 1 次用 TurnOnceUsed 控制。
/// </summary>
public class ST22_001_Ace_Newgate : IScriptedEffect
{
    public string CardNumber => "ST22-001";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 每回合 1 次
        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 候选：手牌中含〈白胡子海盗团〉特征的卡
        var cands = me.Hand.Where(c => c.Info.HasKeyword("白胡子海盗团")).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "艾斯&纽哥特【启动主要】：公开手牌中 1 张〈白胡子海盗团〉，抽 1 张并将公开的卡放回卡组最上方？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RevealHand",
            "公开手牌中 1 张含〈白胡子海盗团〉特征的卡",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (chosen.Count < 1) return; // 未完成公开 → 成本未支付

        var revealed = cands.First(c => c.Id.ToString() == chosen[0]);

        // 向对手公开
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new List<string> { revealed.Info.Number });

        me.TurnOnceUsed.Add(key);

        // 抽 1 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

        // 将公开的卡放回卡组最上方
        if (me.Hand.Contains(revealed))
        {
            me.Hand.Remove(revealed);
            me.Deck.Insert(0, revealed);
        }
    }
}
