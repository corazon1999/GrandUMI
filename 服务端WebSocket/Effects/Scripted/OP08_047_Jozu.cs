using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-047 乔兹（角色 / cost6 power7000）
/// 【登场时】可以将我方1张此角色以外的角色放回其持有者的手牌：
///   将场上最多1张费用不高于6的角色放回其持有者的手牌。
///
/// 旧 DSL 占位用 prompt "OpponentLeaderOrCharacter"（只能选对方）且漏了可选成本，吻合用户 #78
///   "只能选对方角色 / 粗体成本段未执行"。改脚本：
///   - 可选成本：选我方1张（此角色以外）角色回手(BounceToHand)。无其它我方角色则不可发动。
///   - 收益：选场上≤1张费用≤6角色（我方或对方均可，自身除外）放回其持有者手牌。
/// </summary>
public class OP08_047_Jozu : IScriptedEffect
{
    public string CardNumber => "OP08-047";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本候选：我方此角色以外的角色
        var costCands = me.Characters.Where(c => c.Id != self.Id).ToList();
        if (costCands.Count == 0) return; // 无其它我方角色可回手 → 不可发动

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "乔兹【登场时】：将我方1张(此角色以外)角色放回手牌，将场上最多1张费用≤6角色放回其持有者手牌？");
        if (!use) return;

        // 成本：选我方1张(此角色以外)角色放回手牌
        var cExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = costCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var cPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方1张(此角色以外)角色放回手牌作为成本", costCands.Select(c => c.Id.ToString()).ToList(), 1, 1, cExtra);
        if (cPick.Count < 1) return;
        var costCard = costCands.First(c => c.Id.ToString() == cPick[0]);
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, costCard);

        // 收益：场上最多1张费用≤6角色(我方除自身/对方均可)放回其持有者手牌
        var targets = new List<(int owner, CardInstance card)>();
        foreach (var c in me.Characters.Where(c => c.Id != self.Id && c.Info.Cost <= 6))
            targets.Add((ctx.OwnerIndex, c));
        foreach (var c in opp.Characters.Where(c => c.Info.Cost <= 6))
            targets.Add((1 - ctx.OwnerIndex, c));
        if (targets.Count == 0) return;

        var tExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = targets.Select(t => new { id = t.card.Id.ToString(), number = t.card.Info.Number }).ToList(),
        };
        var tPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将场上最多1张费用≤6角色放回其持有者手牌", targets.Select(t => t.card.Id.ToString()).ToList(), 0, 1, tExtra);
        if (tPick.Count > 0)
        {
            var picked = targets.First(t => t.card.Id.ToString() == tPick[0]);
            AtomicOps.BounceToHand(ctx.State, picked.owner, picked.card);
        }
    }
}
