using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-082 五老星（角色）
/// 【启动主要】我方领袖为"伊姆"的场合，可以将我方的1张咚!!转为休息状态，并丢弃我方的1张手牌：
///   将我方所有角色放置到废弃区，并将我方废弃区中最多5张力量为5000且卡牌名称不同的
///   拥有《五老星》特征的角色卡牌登场。
///
/// 实现说明 / 简化点：
///   - 仅当领袖为"伊姆"时可发动。
///   - 成本：将 1 张主动咚!!转为休息 + 丢弃 1 张手牌。
///   - "卡牌名称不同"通过逐张选择并实时排除已选名称实现，最多 5 张。
///   - "我方所有角色放置到废弃区"包含本卡自身（五老星）；KO 后再从废弃区登场。
/// </summary>
public class OP13_082_FiveElders : IScriptedEffect
{
    public string CardNumber => "OP13-082";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (!me.Leader.Info.NameContains("伊姆")) return;

        // 成本可支付性检查：≥1 张主动咚 且 ≥1 张手牌
        if (me.ActiveDonCount < 1 || me.Hand.Count < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "五老星【启动主要】：横置 1 张咚!!并丢弃 1 张手牌，将我方所有角色弃置，再从废弃区登场最多 5 张力量5000且名称互不相同的《五老星》角色？");
        if (!use) return;

        // 成本：将 1 张主动咚!!转为休息（ReturnDonToDeck 不符，改用横置：从 CostArea 找一张 Active 咚置为 Rest）
        var activeDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (activeDon == null) return;
        activeDon.State = DonState.Rest;

        // 成本：丢弃 1 张手牌
        var handExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var handPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, handExtra);
        if (handPick.Count > 0)
        {
            var hc = me.Hand.First(c => c.Id.ToString() == handPick[0]);
            AtomicOps.DiscardHand(me, hc);
        }

        // 效果1：将我方所有角色放置到废弃区
        foreach (var ch in me.Characters.ToList())
        {
            AtomicOps.KO(ctx.State, ctx.OwnerIndex, ch);
        }

        // 效果2：从废弃区登场最多 5 张力量5000且名称不同的《五老星》角色
        var chosenNames = new HashSet<string>();
        for (int i = 0; i < 5; i++)
        {
            var cands = me.Trash.Where(c =>
                c.Info.Kind == CardKind.Character &&
                c.Info.Power == 5000 &&
                c.Info.HasKeyword("五老星") &&
                !chosenNames.Contains(c.Info.Name)
            ).ToList();
            if (cands.Count == 0) break;

            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrashCharacter",
                $"从废弃区登场《五老星》角色（名称互不相同，已选 {i} 张，最多 5 张）",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (pick.Count == 0) break;

            var picked = cands.First(c => c.Id.ToString() == pick[0]);
            chosenNames.Add(picked.Info.Name);
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
