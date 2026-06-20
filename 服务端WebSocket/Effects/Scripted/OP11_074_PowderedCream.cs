using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-074 糖粉奶油末（角色 3 费 3000，大妈海盗团）
/// 【启动主要】【每回合1次】咚!!-1，可以将此角色转为休息状态：宣言任意的费用，
///   并公开对方卡组最上方的 1 张卡牌。公开的卡牌费用与宣言的费用相同的场合，
///   将对方最多 1 张费用不高于 4 的角色转为休息状态。
///
/// 实现说明 / 简化点：
///   - 成本：咚!!-1（须有可放回的咚）+ 将此角色转为休息状态（须为活跃）。每回合 1 次。
///   - "宣言任意费用"用 ChooseOption（0..10）；公开 opp.Deck[0]，比较其卡面原费用。
///   - 命中：让对方最多 1 张当前费用 ≤4 的角色转为休息状态（CurrentCostOf 评估）。
/// </summary>
public class OP11_074_PowderedCream : IScriptedEffect
{
    public string CardNumber => "OP11-074";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        if (self.IsTapped) return;
        // 咚!!-1 须有 1 张可放回的咚
        if (me.CostArea.Count < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "糖粉奶油末【启动主要】：咚!!-1并将此角色转为休息，宣言费用并公开对方卡组顶 1 张，命中则休息对方 1 张费用≤4角色？");
        if (!use) return;

        // 成本
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        AtomicOps.RestCard(self);
        me.TurnOnceUsed.Add(key);

        // 宣言任意费用
        var costOptions = Enumerable.Range(0, 11).Select(i => i.ToString()).ToList();
        int declared = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "宣言任意的费用", costOptions);

        // 公开对方卡组最上方 1 张
        if (opp.Deck.Count == 0) return;
        var revealed = opp.Deck[0];
        if (revealed.Info.Cost != declared) return; // 未命中

        // 命中：将对方最多 1 张当前费用 ≤4 的角色转为休息状态
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 4).ToList();
        if (cands.Count == 0) return;
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "命中！将对方最多 1 张费用≤4的角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
