using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-092 约瑟夫（角色 / CP0）
/// 【登场时】可以将我方废弃区中 2 张拥有的特征中包含 &lt;CP&gt; 的卡牌自选顺序放回卡组最下方：
///   将对方最多 1 张费用不高于 1 的角色 KO。
///
/// 说明 / 简化点：
///   - 可选成本：废弃区需有 ≥2 张含 &lt;CP&gt; 特征的牌，选 2 张放回卡组最下方（按所选顺序放底）。
///   - 收益：KO 对方最多 1 张费用≤1 的角色。
///   - "拥有的特征中包含&lt;CP&gt;"用关键词包含 "CP" 子串判定（如 CP0/CP9 等均含 CP）。
/// </summary>
public class OP07_092_Josef : IScriptedEffect
{
    public string CardNumber => "OP07-092";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本前置条件：废弃区 ≥2 张含 <CP> 特征
        var cpCards = me.Trash.Where(c =>
            c.Info.Keywords.Any(k => k.Contains("CP"))).ToList();
        if (cpCards.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "约瑟夫【登场时】：将废弃区 2 张含<CP>特征的牌放回卡组最下方，KO 对方 1 张费用≤1 的角色？");
        if (!use) return;

        // 成本：选 2 张含 <CP> 特征的废弃区牌放回卡组最下方
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cpCards
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var picked = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrashCard",
            "将废弃区 2 张含<CP>特征的牌自选顺序放回卡组最下方",
            cpCards.Select(c => c.Id.ToString()).ToList(), 2, 2, extra);
        if (picked.Count < 2) return; // 成本未支付

        foreach (var cid in picked)
        {
            var card = cpCards.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null) AtomicOps.ReturnTrashToDeckBottom(me, card);
        }

        // 收益：KO 对方最多 1 张费用≤1 的角色
        var koCands = opp.Characters.Where(c => c.Info.Cost <= 1).ToList();
        if (koCands.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤1 的角色 KO",
            koCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = koCands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
