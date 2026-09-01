using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-023 乌塔（角色 / 风 4 费 5000，FILM）
/// 【登场时】将我方最多2张咚!!转为活跃状态。之后，本回合中，我方无法登场原本的费用为5或更高的角色卡牌。
/// 【KO时】将我方手牌中最多1张费用不高于5的角色卡牌以休息状态登场。
///
/// 实现：
///   - 【登场时】把最多 2 张休息状态的咚!! 转为活跃（咚之间无区别，无需玩家选择）。
///   - 【KO时】从手牌选最多 1 张原本费用 ≤5 的角色，以休息状态免费登场
///     （手牌私有区，经 extra.choiceCards 下发卡面）。
///
///   - 费用 5 以上角色禁登场由玩家级标记与所有登场入口统一拦截。
/// </summary>
public class OP13_023_Uta : IScriptedEffect
{
    public string CardNumber => "OP13-023";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 将我方最多 2 张休息状态的咚!! 转为活跃状态
            int activated = 0;
            foreach (var d in me.CostArea)
            {
                if (activated >= 2) break;
                if (d.State == DonState.Rest)
                {
                    d.State = DonState.Active;
                    d.AttachedToCardId = null;
                    activated++;
                }
            }
            ctx.State.NoPlayCharacterOriginalCostGteThisTurn[ctx.OwnerIndex] = 5;
            return;
        }

        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            // 将我方手牌中最多 1 张原本费用 ≤5 的角色以休息状态登场
            var candidates = me.Hand
                .Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 5)
                .ToList();
            if (candidates.Count == 0) return;

            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = candidates
                    .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                    .ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
                "选最多 1 张费用≤5的角色以休息状态登场",
                candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count == 0) return;

            var picked = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
            if (picked is null) return;

            await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
            // 以休息状态登场
            if (me.Characters.Contains(picked)) AtomicOps.RestCard(picked);
        }
    }
}
