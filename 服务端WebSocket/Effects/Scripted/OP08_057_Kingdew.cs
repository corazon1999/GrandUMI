using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-057 烬（领航/领袖类，此处按角色脚本处理）
/// 【启动主要】【每回合1次】咚!!-2：选择以下的 1 项。
///   ・我方手牌不多于 5 张的场合，抽取 1 张卡牌。
///   ・本回合中，对方最多 1 张角色费用 -2。
///
/// 实现说明：
///   - 成本咚!!-2 用 ReturnDonToDeck(me, 2)；需 cost 区 ≥2 张咚才能发动。
///   - 二选一用 ChooseOption；第 1 项受"手牌≤5"条件约束。
///   - 费用-2 本回合用 AddCostModifier(c, -2, ThisTurn)。
/// </summary>
public class OP08_057_Kingdew : IScriptedEffect
{
    public string CardNumber => "OP08-057";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合 1 次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本：咚!!-2（合计存在的咚需 ≥2）
        if (me.TotalDonInCostArea < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "烬【启动主要】：咚!!-2，选择 1 项（抽 1 张 / 对方 1 张角色费用-2）？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 2)) return;
        me.TurnOnceUsed.Add(key);

        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择以下的 1 项",
            new[] { "我方手牌≤5 时抽 1 张", "本回合对方 1 张角色费用-2" });

        if (opt == 0)
        {
            if (me.Hand.Count <= 5)
                AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        }
        else
        {
            var cands = opp.Characters.ToList();
            if (cands.Count == 0) return;
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张对方角色，本回合费用-2",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddCostModifier(tgt, -2, KeywordDuration.ThisTurn);
            }
        }
    }
}
