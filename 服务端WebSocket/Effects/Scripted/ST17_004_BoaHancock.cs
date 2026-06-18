using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST17-004 波尔·汉库珂（角色 / 水 / 王下七武海・九蛇海盗团，cost4 power6000）
/// 【阻挡者】（关键词，引擎处理）
/// 【登场时】确认我方卡组最上方的 3 张卡牌，自选顺序排列放置到卡组最上方或最下方；
///            之后，赋予我方 1 张拥有《王下七武海》特征的领袖或角色最多 1 张休息状态的咚!!。
///
/// 实现说明：
///   - 观星（顶 3 张重排放顶/底）：复用 AtomicOps.ReorderTopK（同 EB03-023 嘉雅 / EB02-023 克洛克达尔）。
///     "自选顺序"按玩家点选先后形成相对顺序；放顶/底由 ChooseOption 决定。
///   - 贴咚：从我方费用区取休息咚赋给所选《王下七武海》领袖/角色（只用休息咚，不回退活跃）。
///     费用区无休息咚时直接跳过该选择（无事发生）——避免 GM 不付费召唤等场景误吃活跃咚。
/// </summary>
public class ST17_004_BoaHancock : IScriptedEffect
{
    public string CardNumber => "ST17-004";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // ① 观星：确认卡组顶 3 张，自选顺序排列后放置到卡组最上方或最下方
        int k = Math.Min(3, me.Deck.Count);
        if (k > 0)
        {
            var top = me.Deck.Take(k).ToList();
            var scryExtra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            // 让玩家自选这 k 张的顺序（按点选先后形成相对顺序）
            var ordered = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "BoaHancockScry",
                "确认卡组顶 3 张，自选顺序排列后放置到卡组最上方或最下方",
                top.Select(c => c.Id.ToString()).ToList(), k, k, scryExtra);

            int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "将这些卡牌放置到卡组的位置", new[] { "最上方", "最下方" });

            // 按玩家选择的顺序映射回 Guid；兜底补齐未返回的原序
            var order = ordered
                .Select(id => top.FirstOrDefault(c => c.Id.ToString() == id))
                .Where(c => c is not null)
                .Select(c => c!.Id)
                .ToList();
            foreach (var c in top)
                if (!order.Contains(c.Id)) order.Add(c.Id);

            AtomicOps.ReorderTopK(me, order, toBottom: where == 1);
        }

        // ② 赋予我方 1 张《王下七武海》领袖或角色最多 1 张休息咚
        // 无休息咚可赋予则直接跳过该选择（无事发生），不弹无意义的提示、也不消耗活跃咚
        if (!me.CostArea.Any(d => d.State == DonState.Rest)) return;

        var cands = new List<CardInstance> { me.Leader };
        cands.AddRange(me.Characters);
        cands = cands.Where(c => c.Info.HasKeyword("王下七武海")).ToList();
        if (cands.Count == 0) return;

        var donExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "赋予我方1张《王下七武海》领袖或角色最多1张休息咚",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, donExtra);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AttachDonFromCost(me, tgt.Id, 1, DonState.Rest);
        }
    }
}
