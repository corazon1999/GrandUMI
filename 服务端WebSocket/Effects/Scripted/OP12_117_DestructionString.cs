using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-117 破坏之弦（事件）
/// 【反击】本次战斗中，我方领袖力量+3000。
///   —— 直接对我方领袖施加本次战斗力量+3000。
///
/// </summary>
public class OP12_117_DestructionString : IScriptedEffect
{
    public string CardNumber => "OP12-117";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            AtomicOps.AddPowerThisBattle(me.Leader, 3000);
            return;
        }
        if (!me.Leader.Info.HasKeyword("超新星")) return;
        var activeDon = me.CostArea.Where(don => don.State == DonState.Active).ToList();
        if (activeDon.Count < 5) return;
        var paid = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RestOwnDon",
            "选择 5 张活跃咚转为休息状态，或取消发动",
            activeDon.Select(don => don.Id.ToString()).ToList(), 0, 5,
            new Dictionary<string, object?>
            {
                ["donChoices"] = activeDon.Select(don => new { id = don.Id.ToString(), state = don.State.ToString() }).ToList(),
                ["canCancel"] = true,
            });
        if (paid.Count < 5) return;
        foreach (var id in paid.Take(5))
        {
            var don = activeDon.FirstOrDefault(item => item.Id.ToString() == id);
            if (don is not null) don.State = DonState.Rest;
        }

        var targets = new List<(int Owner, CardInstance Card)>();
        for (int owner = 0; owner < 2; owner++)
            targets.AddRange(ctx.State.Players[owner].Characters
                .Where(card => ctx.State.CurrentCostOf(owner, card) <= 9)
                .Select(card => (owner, card)));
        var selected = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "选择最多 1 张费用不高于 9 的角色，正面朝下加入其持有者生命区",
            targets.Select(item => item.Card.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = targets.Select(item => new { id = item.Card.Id.ToString(), number = item.Card.Info.Number }).ToList(),
            });
        if (selected.Count == 0) return;
        var target = targets.First(item => item.Card.Id.ToString() == selected[0]);
        int position = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "放到生命区哪个位置？",
            new[] { "最上方", "最下方" });
        AtomicOps.MoveCharToLife(ctx.State, target.Owner, target.Card, position == 0);
        target.Card.IsLifeFaceUp = false;
    }
}
