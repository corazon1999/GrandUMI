using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-105 月光·莫利亚（角色 / 光 / 6 费 / 7000 / 王下七武海・恐怖之船海盗团）
/// 【触发】我方生命卡牌不多于 1 张的场合，将我方废弃区中费用不高于 4 的
///   "阿布萨罗姆"、"豪格巴克医生"和"佩罗娜"各最多 1 张登场。
///
/// 实现说明 / 简化点：
///   - 该效果为生命触发，仅响应 OnLifeRevealTrigger。
///   - 条件：发动时我方生命牌 ≤ 1（生命触发结算时本卡通常尚未计入生命）。
///   - 三个不同卡名各最多 1 张，分别进行 3 次独立选择 + PlayFromTrashFree。
///   - 费用判定取卡面原始费用 c.Info.Cost ≤ 4。
///   - 角色区上限 5，由 PlayFromTrashFree 内部处理（满则牺牲最左侧）。
/// </summary>
public class OP16_105_Moria : IScriptedEffect
{
    public string CardNumber => "OP16-105";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方生命牌 ≤ 1
        if (me.LifeCount > 1) return;

        string[] names = { "阿布萨罗姆", "豪格巴克医生", "佩罗娜" };

        foreach (var name in names)
        {
            var cands = me.Trash
                .Where(c => c.Info.Kind == CardKind.Character
                            && c.Info.Cost <= 4
                            && c.MatchesName(name))
                .ToList();
            if (cands.Count == 0) continue;

            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands
                    .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                    .ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "MoriaPlayFromTrash",
                $"将废弃区中费用≤4 的“{name}”最多 1 张登场",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
            }
        }
    }
}
