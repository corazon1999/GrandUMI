using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-026 杰丽·邦妮（角色）
/// 【登场时】对方最多 1 张处于休息状态的角色或咚!!，在下个对方的重置阶段不会转为活跃状态。
///
/// 实现：将对方休息角色与休息咚放入同一候选列表；角色与咚分别设置各自的
/// CannotActivateNextReset 一次性标记，由重置阶段消费并跳过本次激活。
/// </summary>
public class OP07_026_JewelryBonney : IScriptedEffect
{
    public string CardNumber => "OP07-026";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var characterCandidates = opp.Characters.Where(c => c.IsTapped).ToList();
        var donCandidates = opp.CostArea.Where(d => d.State == DonState.Rest).ToList();
        if (characterCandidates.Count + donCandidates.Count == 0) return;

        var validChoices = characterCandidates.Select(c => c.Id.ToString())
            .Concat(donCandidates.Select(d => d.Id.ToString()))
            .ToList();
        var extra = new Dictionary<string, object?>
        {
            ["donChoices"] = donCandidates.Select(d => new
            {
                id = d.Id.ToString(),
                state = d.State.ToString(),
            }).ToList(),
        };

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentRestingCharacterOrDon",
            "选择对方最多 1 张休息状态的角色或咚!!，使其在下个对方的重置阶段不会转为活跃",
            validChoices, 0, 1, extra);
        if (chosen.Count == 0) return;

        var chosenId = chosen[0];
        var character = characterCandidates.FirstOrDefault(c => c.Id.ToString() == chosenId);
        if (character is not null)
        {
            AtomicOps.PreventActivateNextReset(character);
            return;
        }

        var don = donCandidates.FirstOrDefault(d => d.Id.ToString() == chosenId);
        if (don is not null) don.CannotActivateNextReset = true;
    }
}
