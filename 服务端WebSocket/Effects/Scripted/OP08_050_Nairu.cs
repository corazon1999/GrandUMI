using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-050 耐休尔（角色）
/// 【阻挡者】（引擎处理关键词，无需脚本）
/// 【登场时】抽取 2 张卡牌，将我方 2 张手牌自选顺序排列并放回卡组最上方或最下方。
///
/// 实现说明 / 简化点：
///   - 先抽 2 张。
///   - 让玩家选择 2 张手牌；再用 ChooseOption 选择整体放回卡组最上方或最下方。
///   - "自选顺序排列" 实现为按玩家选择列表的顺序放置（放最上方时第 1 张在最顶）。
/// </summary>
public class OP08_050_Nairu : IScriptedEffect
{
    public string CardNumber => "OP08-050";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 抽 2 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

        // 抽牌后手牌不足 2 张时，仍须将现有手牌全部放回；最多放回 2 张。
        int returnCount = Math.Min(2, me.Hand.Count);
        if (returnCount == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            $"选择 {returnCount} 张手牌放回卡组（自选顺序）",
            me.Hand.Select(c => c.Id.ToString()).ToList(), returnCount, returnCount);
        if (chosen.Count < returnCount) return;

        // 按玩家选择的顺序取出这 2 张
        var picks = chosen
            .Select(id => me.Hand.First(c => c.Id.ToString() == id))
            .ToList();

        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            $"将这 {returnCount} 张手牌放回卡组最上方或最下方",
            new[] { "放回最上方", "放回最下方" });

        foreach (var c in picks) me.Hand.Remove(c);

        if (opt == 0)
        {
            // 放最上方：第 1 张在最顶
            for (int i = picks.Count - 1; i >= 0; i--) me.Deck.Insert(0, picks[i]);
        }
        else
        {
            // 放最下方
            me.Deck.AddRange(picks);
        }
    }
}
