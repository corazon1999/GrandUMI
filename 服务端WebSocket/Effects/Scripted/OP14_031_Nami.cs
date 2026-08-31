using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-031 奈美
/// 【登场时】将对方最多2张费用不高于8的角色转为休息状态。
/// 之后，当本回合结束时，将我方最多5张咚!!转为活跃状态。
/// </summary>
public sealed class OP14_031_Nami : IScriptedEffect
{
    public string CardNumber => "OP14-031";

    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opponent = ctx.State.Players[1 - ctx.OwnerIndex];
        var candidates = opponent.Characters
            .Where(card => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, card) <= 8)
            .ToList();

        if (candidates.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多2张费用不高于8的角色转为休息状态",
                candidates.Select(card => card.Id.ToString()).ToList(), 0, 2,
                new Dictionary<string, object?>
                {
                    ["choiceCards"] = candidates
                        .Select(card => new { id = card.Id.ToString(), number = card.Info.Number })
                        .ToList(),
                });
            foreach (var id in chosen.Distinct())
            {
                var target = candidates.FirstOrDefault(card => card.Id.ToString() == id);
                if (target is not null) AtomicOps.RestCard(target);
            }
        }

        ctx.State.EndOfTurnTasks.Add(new EndTurnTask
        {
            // 使用独立任务种类，保留旧快照及其他固定数量 RefreshOwnDon 的原有语义。
            Kind = "ChooseRefreshOwnDonUpTo",
            Owner = ctx.OwnerIndex,
            SourceCardId = ctx.Source.Id.ToString(),
            Count = 5,
        });
    }
}
