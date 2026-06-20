using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-109 帕帕格（角色 / 光 1 费 0 力量，动物/鱼人岛）
/// 【登场时】我方场上存在"凯米"的场合，抽取 2 张卡牌，丢弃我方的 2 张手牌。
///
/// 实现：
///   - 登场时检测我方角色中是否存在卡名为"凯米"的角色（MatchesName）。
///   - 满足则抽 2 张，然后玩家自选丢弃 2 张手牌（强制；手牌不足时尽量丢）。
///   - 废弃候选通过 extra.choiceCards 显示卡面。
/// </summary>
public class OP11_109_Pappag : IScriptedEffect
{
    public string CardNumber => "OP11-109";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方场上存在"凯米"
        bool hasKami = me.Characters.Any(c => c.MatchesName("凯米"));
        if (!hasKami) return;

        // 抽 2 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

        // 丢弃我方 2 张手牌
        int toDiscard = Math.Min(2, me.Hand.Count);
        if (toDiscard == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PappagDiscard",
            "丢弃我方 2 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), toDiscard, toDiscard, extra);

        if (chosen.Count > 0)
        {
            foreach (var cid in chosen)
            {
                var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == cid);
                if (card is not null) AtomicOps.DiscardHand(me, card);
            }
        }
        else
        {
            // 超时未选 → 自动从前面丢
            for (int i = 0; i < toDiscard && me.Hand.Count > 0; i++)
                AtomicOps.DiscardHand(me, me.Hand[0]);
        }
    }
}
