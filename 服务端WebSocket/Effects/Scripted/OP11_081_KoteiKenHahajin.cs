using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-081 皇帝剑破破刃（事件）
/// 【主要】宣言任意的费用，并公开对方卡组最上方的 1 张卡牌。
///   公开的卡牌费用与宣言的费用相同的场合，将对方最多 1 张原本的费用不高于 8 的角色 KO。
///
/// 说明 / 简化点：
/// - "宣言任意的费用"实现为让玩家在 0~10 中选择一个数字。
/// - "公开对方卡组最上方 1 张"读取对方 Deck[0]，比较卡面原本费用 Info.Cost。
/// - 命中则可 KO 对方 1 张"原本的费用"(Info.Cost) ≤8 的角色。
/// </summary>
public class OP11_081_KoteiKenHahajin : IScriptedEffect
{
    public string CardNumber => "OP11-081";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 宣言任意费用（0~10）
        var options = new List<string>();
        for (int i = 0; i <= 10; i++) options.Add(i.ToString());
        int declared = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "宣言任意的费用", options);

        // 公开对方卡组最上方 1 张
        if (opp.Deck.Count == 0) return;
        var revealed = opp.Deck[0];

        // 命中：公开卡的原本费用 == 宣言费用
        if (revealed.Info.Cost != declared) return;

        // 将对方最多 1 张原本费用 ≤8 的角色 KO
        var cands = opp.Characters.Where(c => c.Info.Cost <= 8).ToList();
        if (cands.Count == 0) return;
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张原本费用 ≤8 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
