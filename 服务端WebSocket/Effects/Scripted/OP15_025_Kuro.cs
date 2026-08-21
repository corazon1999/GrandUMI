using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-025 克洛（角色）。
/// 【登场时】赋予对方一张角色最多两张对方费用区中休息状态的咚；
/// 之后，在本回合结束时，可以选择对方一张被赋予三张或更多咚且处于休息状态的角色，
/// 使其在下一个对方重置阶段中不会转为活跃状态。
/// </summary>
public class OP15_025_Kuro : IScriptedEffect
{
    public string CardNumber => "OP15-025";

    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opponent = ctx.State.Players[1 - ctx.OwnerIndex];

        if (opponent.Characters.Count > 0)
        {
            var targets = opponent.Characters;
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = targets
                    .Select(card => new { id = card.Id.ToString(), number = card.Info.Number })
                    .ToList(),
            };
            var picked = await ctx.Prompts.ChooseCards(
                ctx.OwnerIndex,
                "OpponentCharacter",
                "选择对方一张角色，赋予其最多两张对方费用区中休息状态的咚",
                targets.Select(card => card.Id.ToString()).ToList(),
                0,
                1,
                extra);
            if (picked.Count > 0)
            {
                var target = targets.First(card => card.Id.ToString() == picked[0]);
                AtomicOps.AttachDonFromCost(opponent, target.Id, 2, DonState.Rest);
            }
        }

        // “之后”这一段要等到本回合结束时才检查目标是否满足条件。
        ctx.State.EndOfTurnTasks.Add(new EndTurnTask
        {
            Kind = "PreventOpponentDonCharacterReset",
            Owner = ctx.OwnerIndex,
            SourceCardId = ctx.Source.Id.ToString(),
        });
    }
}
