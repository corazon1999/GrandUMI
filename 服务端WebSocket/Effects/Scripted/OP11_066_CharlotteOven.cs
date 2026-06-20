using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-066 夏洛特·烤箱（角色 4 费 5000，大妈海盗团）
/// 【启动主要】可以将此角色转为休息状态：宣言任意的费用，并公开对方卡组最上方的 1 张卡牌。
///   公开的卡牌费用与宣言的费用相同的场合，将对方最多 1 张原本的费用不高于 3 的角色 KO。
///   之后，从咚!!卡组中追加最多 1 张休息状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 成本：将此角色转为休息状态（须为活跃）。
///   - "宣言任意的费用"用 ChooseOption 让玩家从 0..10 中选一个数。
///   - "公开对方卡组最上方 1 张"：取 opp.Deck[0]，比较其卡面原本费用 (Info.Cost) 与宣言费用。
///     （公开为机械步骤，结果由后续效果体现。）
///   - 命中时：KO 对方最多 1 张原本费用 ≤3 的角色；随后从咚卡组追加 1 张休息咚。
/// </summary>
public class OP11_066_CharlotteOven : IScriptedEffect
{
    public string CardNumber => "OP11-066";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;
        int oppIdx = 1 - ctx.OwnerIndex;

        if (self.IsTapped) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "烤箱【启动主要】：将此角色转为休息，宣言费用并公开对方卡组顶 1 张，命中则 KO 对方 1 张原费用≤3角色并追加 1 张休息咚？");
        if (!use) return;

        // 成本：转为休息状态
        AtomicOps.RestCard(self);

        // 宣言任意费用（0..10）
        var costOptions = Enumerable.Range(0, 11).Select(i => i.ToString()).ToList();
        int declared = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "宣言任意的费用", costOptions);

        // 公开对方卡组最上方 1 张
        if (opp.Deck.Count == 0) return;
        var revealed = opp.Deck[0];
        if (revealed.Info.Cost != declared) return; // 未命中

        // 命中：KO 对方最多 1 张原本费用 ≤3 的角色
        var cands = opp.Characters.Where(c => c.Info.Cost <= 3).ToList();
        if (cands.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "命中！将对方最多 1 张原费用≤3的角色 KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, oppIdx, tgt);
            }
        }

        // 之后：从咚卡组追加 1 张休息咚
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
    }
}
