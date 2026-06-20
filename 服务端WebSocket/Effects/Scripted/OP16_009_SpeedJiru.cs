using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-009 斯比德·基尔（角色）
/// 【登场时】可以丢弃我方手牌中1张力量为8000的角色卡牌：
///   直到下个对方的结束阶段结束时为止，此角色获得【速攻】效果，且力量+2000。
///
/// 实现说明 / 简化点：
///   - 可选成本(丢弃手牌中1张力量8000角色)与收益强绑定，用脚本：ConfirmOptional → 选牌丢弃 → 给收益。
///   - 【速攻】用 GiveKeyword(KeywordDuration.UntilNextOpponentEndPhase) 精确表达持续时长。
///   - 力量+2000：AtomicOps 力量增量仅有 ThisTurn/ThisBattle/Persistent，无"至对方结束阶段"档，
///     此处用 AddPowerThisTurn 近似（同一回合内有效，影响极小）。
/// </summary>
public class OP16_009_SpeedJiru : IScriptedEffect
{
    public string CardNumber => "OP16-009";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本候选：手牌中力量为 8000 的角色
        var cost = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.Power == 8000).ToList();
        if (cost.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "斯比德·基尔【登场时】：丢弃手牌中1张力量8000的角色，使此角色获得【速攻】且力量+2000？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cost.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardCost",
            "丢弃手牌中1张力量8000的角色作为成本",
            cost.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (chosen.Count < 1) return; // 未支付成本

        var discard = cost.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.DiscardHand(me, discard);

        // 收益：获得【速攻】（至下个对方结束阶段）+ 力量+2000
        AtomicOps.GiveKeyword(self, "速攻", KeywordDuration.UntilNextOpponentEndPhase);
        AtomicOps.AddPowerThisTurn(self, 2000);
    }
}
