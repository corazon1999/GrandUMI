using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-062 温思默克·强智（角色 / 暗 8 费 8000）
/// 【登场时】咚!!1（可以将我方场上指定数量的咚‼放回咚‼卡组），可以丢弃我方的 2 张手牌：
///   将我方废弃区中最多 4 张卡牌名称不同的力量不高于 4000 且拥有《GERMA 66》特征的角色卡牌登场。
/// 【启动主要】【每回合1次】咚!!1：将对方最多 1 张咚!! 转为休息状态。
///
/// 实现说明 / 简化点：
///   - 【登场时】"咚!!1"为括注的可选成本（放回咚），按惯例只实现核心收益，
///     主要成本"丢弃 2 张手牌"作为发动条件实现；不足 2 张则无法发动。
///   - "名称不同"：依次选择，过滤掉已选名称，最多 4 张。
///   - 【启动主要】成本"咚!!1"= 将我方 1 张咚放回咚卡组（ReturnDonToDeck）。
///     "将对方咚转休息"直接操作对方 CostArea 中处于活跃状态的咚。
/// </summary>
public class OP06_062_VinsmokeNiji : IScriptedEffect
{
    public string CardNumber => "OP06-062";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 成本：可以丢弃 2 张手牌（自身已登场，不含自身则需 ≥2 张其他手牌）
            var handChoices = me.Hand.Where(c => c.Id != self.Id).ToList();
            if (handChoices.Count < 2) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "温思默克·强智【登场时】：丢弃 2 张手牌，从废弃区登场最多 4 张名称不同、力量≤4000 的《GERMA 66》角色？");
            if (!use) return;

            var discard = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
                "丢弃 2 张手牌",
                handChoices.Select(c => c.Id.ToString()).ToList(), 2, 2);
            if (discard.Count < 2) return;
            foreach (var cid in discard)
            {
                var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == cid);
                if (card is not null) AtomicOps.DiscardHand(me, card);
            }

            // 从废弃区登场最多 4 张名称不同、力量≤4000 的《GERMA 66》角色
            var usedNames = new HashSet<string>();
            for (int i = 0; i < 4; i++)
            {
                var cands = me.Trash.Where(c =>
                    c.Info.Kind == CardKind.Character &&
                    c.Info.Power <= 4000 &&
                    c.Info.HasKeyword("GERMA 66") &&
                    !usedNames.Contains(c.Info.Name)
                ).ToList();
                if (cands.Count == 0) break;

                var extra = new Dictionary<string, object?>
                {
                    ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                };
                var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
                    $"从废弃区登场名称不同的《GERMA 66》角色（已登场 {i} 张，最多 4 张，选 0 张结束）",
                    cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
                if (ch.Count == 0) break;
                var picked = cands.First(c => c.Id.ToString() == ch[0]);
                usedNames.Add(picked.Info.Name);
                AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
            }
            return;
        }

        // ── 【启动主要】【每回合1次】咚!!1：将对方最多 1 张咚!! 转为休息状态 ──
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本：咚!!1（我方放回 1 张咚到咚卡组）
        if (me.CostArea.Count < 1) return;

        // 目标：对方处于活跃状态的咚
        var activeDon = opp.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (activeDon.Count == 0) return;

        bool useAct = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "温思默克·强智【启动主要】：咚!!-1，将对方 1 张咚!! 转为休息状态？");
        if (!useAct) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        me.TurnOnceUsed.Add(key);

        // 将对方 1 张活跃咚转为休息
        activeDon[0].State = DonState.Rest;
    }
}
