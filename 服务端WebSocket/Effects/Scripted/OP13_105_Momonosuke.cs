using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-105 光月桃之助（角色 / 光 / 3 费 / 0 / 和之国・光月家）
/// 【登场时】确认我方所有的生命卡牌，自选顺序放回生命区。
///
/// 实现：
///   - 让玩家查看自己全部生命牌（经 extra.choiceCards 下发卡面，生命区私有），
///     并按期望的“从最上方到最下方”的顺序依次选择全部生命牌（min=max=生命数）。
///   - 按所选顺序重建 LifeArea（索引 0 = 最上方）。
///   - 生命数 ≤ 1 时无需重排，直接返回。若玩家未按要求选满（超时等），保持原顺序。
/// </summary>
public class OP13_105_Momonosuke : IScriptedEffect
{
    public string CardNumber => "OP13-105";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var life = me.LifeArea.ToList();
        if (life.Count <= 1) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = life
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        // 按从最上方到最下方的顺序依次选择全部生命牌
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Momonosuke105Reorder",
            "确认我方所有生命卡牌，按从最上方到最下方的顺序重新排列",
            life.Select(c => c.Id.ToString()).ToList(), life.Count, life.Count, extra);

        // 校验：必须选满且无重复，否则保持原顺序
        if (chosen.Count != life.Count || chosen.Distinct().Count() != life.Count) return;

        var ordered = new List<CardInstance>();
        foreach (var cid in chosen)
        {
            var card = life.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is null) return; // 出现未知 id，放弃重排
            ordered.Add(card);
        }

        me.LifeArea.Clear();
        me.LifeArea.AddRange(ordered);
    }
}
