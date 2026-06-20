using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-054 奈美（角色）
/// 【阻挡者】（关键词，由引擎处理）。
/// 【登场时】我方领袖为多种颜色的场合：抽取 3 张卡牌，
///   将我方的 2 张手牌自选顺序放置到卡组最上方或最下方。
///
/// 实现说明 / 简化点：
///   - "多种颜色"判定为领袖 ColorList.Length >= 2。
///   - "自选顺序放置到卡组顶或底"：逐张选择 1 张手牌，并选择放到卡组顶还是底，共处理 2 张。
///     先放的牌更靠外（放顶时先放的在更上方，放底时先放的在更下方），近似还原"自选顺序"。
///   - 抽牌后若手牌不足 2 张则按实际数量处理。
/// </summary>
public class OP11_054_Nami : IScriptedEffect
{
    public string CardNumber => "OP11-054";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 仅在领袖为多种颜色时发动
        if (me.Leader.Info.ColorList.Length < 2) return;

        // 抽取 3 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 3);

        // 将 2 张手牌自选顺序放到卡组顶/底
        int count = Math.Min(2, me.Hand.Count);
        for (int i = 0; i < count; i++)
        {
            var hand = me.Hand.ToList();
            if (hand.Count == 0) break;

            // 手牌属"不下发身份"区域，须随选择面板下发 {id, number}，否则前端 PromptOverlay 找不到卡号、卡图显示为占位（反馈 #19）
            var handExtra = new Dictionary<string, object?>
            {
                ["choiceCards"] = hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "选择 1 张手牌放回卡组（共 2 张）",
                hand.Select(c => c.Id.ToString()).ToList(), 1, 1, handExtra);
            if (pick.Count == 0) break;

            var card = hand.First(c => c.Id.ToString() == pick[0]);

            int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "将该牌放到卡组最上方还是最下方？", new[] { "卡组最上方", "卡组最下方" });

            me.Hand.Remove(card);
            if (where == 0) me.Deck.Insert(0, card);
            else me.Deck.Add(card);
        }
    }
}
