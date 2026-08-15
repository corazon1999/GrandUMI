using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>ST26-002 乔巴：咚-2后，横置对方最多1张原本费用≤1角色或1张活跃咚。</summary>
public sealed class ST26_002_Chopper : IScriptedEffect
{
    public string CardNumber => "ST26-002";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, ctx.OwnerIndex, 2, optional: true)) return;
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var characters = opp.Characters.Where(card => card.Info.Cost <= 1 && !card.IsTapped).ToList();
        var dons = opp.CostArea.Where(don => don.State == DonState.Active).ToList();
        var ids = characters.Select(card => card.Id.ToString()).Concat(dons.Select(don => don.Id.ToString())).ToList();
        if (ids.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacterOrDon",
            "将对方最多1张原本费用≤1的角色或咚!!转为休息状态", ids, 0, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = characters.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
                ["donChoices"] = dons.Select(don => new { id = don.Id.ToString(), state = don.State.ToString() }).ToList(),
            });
        if (chosen.Count == 0) return;
        var character = characters.FirstOrDefault(card => card.Id.ToString() == chosen[0]);
        if (character is not null) AtomicOps.RestCard(character);
        var don = dons.FirstOrDefault(item => item.Id.ToString() == chosen[0]);
        if (don is not null) don.State = DonState.Rest;
    }
}
