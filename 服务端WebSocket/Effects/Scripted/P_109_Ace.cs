using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-109 波特夹斯·D·艾斯（角色 / 水 / 白胡子海盗团，cost5 power6000，counter1000）
/// 【阻挡者】（关键词，引擎处理）
/// 【登场时】确认我方卡组最上方的 3 张卡牌，将其自选顺序排列并放置到卡组最上方或最下方。
///   之后，赋予我方 1 张领袖或角色最多 1 张休息状态的咚!!。
///
/// 实现说明：
///   - 只实现主动【登场时】部分；【阻挡者】由引擎按关键词处理。
///   - 确认卡组顶 3 张（向我方展示），用 ChooseOption 决定整体放卡组最上方或最下方；
///     「自选顺序」简化为保持原相对顺序。
///   - 之后赋予我方 1 张领袖/角色最多 1 张休息状态的咚!!。
/// </summary>
public class P_109_Ace : IScriptedEffect
{
    public string CardNumber => "P-109";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 确认卡组顶 3 张
        int peek = Math.Min(3, me.Deck.Count);
        if (peek > 0)
        {
            var top = me.Deck.Take(peek).ToList();
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            // 仅用于向玩家展示这 3 张（最少最多 0，不强制选择）
            await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "确认卡组最上方的 3 张卡牌", new List<string>(), 0, 0, extra);

            int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "将这些卡牌放置到卡组的？", new[] { "卡组最上方", "卡组最下方" });

            if (where == 1)
            {
                // 放底：从顶部移除后追加到底部（保持原相对顺序）
                foreach (var c in top) me.Deck.Remove(c);
                me.Deck.AddRange(top);
            }
            // where==0：保持在卡组最上方原顺序，无需操作
        }

        // 之后：赋予我方 1 张领袖或角色最多 1 张休息状态的咚!!
        if (me.CostArea.Any(d => d.State == DonState.Active))
        {
            var targets = new List<CardInstance> { me.Leader };
            targets.AddRange(me.Characters);
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "选择 1 张领袖或角色，赋予最多 1 张休息状态的咚!!",
                targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pick.Count > 0)
            {
                var tgt = targets.First(c => c.Id.ToString() == pick[0]);
                AtomicOps.AttachDonFromCost(me, tgt.Id, 1, DonState.Rest);
            }
        }
    }
}
