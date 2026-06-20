using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-079 克洛克达尔（领袖）
/// 持续：对方所有角色不会因我方的效果而离场（静态防离场机制，引擎无持续通道 → 未实现）。
/// 【启动主要】【每回合1次】可以将我方1张拥有的特征中包含〈巴洛克工作室〉的角色KO：
///   本回合中，对方最多1张角色费用-10。之后，可以将我方卡组最上方的2张卡牌放置到废弃区。
///
/// 实现说明 / 简化点：
///   - 仅实现【启动主要】可脚本部分。发动成本为"KO 我方 1 张含〈巴洛克工作室〉特征的角色"，
///     该成本不在 DSL 既有 cost 键内，故用脚本表达：先 ConfirmOptional，再选我方角色KO作成本。
///   - "持续：对方所有角色不会因我方的效果而离场"为静态机制，引擎暂无该持续通道，未实现。
///   - "可以放置卡组顶2张到废弃区"为可选附加收益，用 ConfirmOptional 询问后磨2张。
/// </summary>
public class OP14_079_Crocodile : IScriptedEffect
{
    public string CardNumber => "OP14-079";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本来源：我方含〈巴洛克工作室〉特征的角色
        var costCands = me.Characters.Where(c => c.Info.HasKeyword("巴洛克工作室")).ToList();
        if (costCands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "克洛克达尔【启动主要】：KO 我方 1 张〈巴洛克工作室〉角色，使对方 1 张角色本回合费用-10？");
        if (!use) return;

        // 选择并 KO 我方 1 张作为成本
        var costPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张〈巴洛克工作室〉角色作为成本KO",
            costCands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (costPick.Count < 1) return; // 未支付成本
        var costCard = costCands.First(c => c.Id.ToString() == costPick[0]);
        AtomicOps.KO(ctx.State, ctx.OwnerIndex, costCard);

        me.TurnOnceUsed.Add(key);

        // 效果1：对方最多1张角色费用-10（本回合）
        if (opp.Characters.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = opp.Characters.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 1 张角色，本回合费用-10",
                opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var tgt = opp.Characters.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddCostModifier(tgt, -10, KeywordDuration.ThisTurn);
            }
        }

        // 效果2：可以将卡组顶2张放置到废弃区
        if (me.Deck.Count > 0)
        {
            bool mill = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "之后，将我方卡组最上方的 2 张卡牌放置到废弃区？");
            if (mill) AtomicOps.MillTop(me, 2);
        }
    }
}
