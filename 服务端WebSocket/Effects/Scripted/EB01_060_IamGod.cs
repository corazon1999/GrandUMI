using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-060 我是……神！！（事件 / 光 / 空岛）
/// 【主要】将我方手牌或废弃区中最多 1 张费用不高于 7 的“艾尼路”登场。
///   之后，将我方生命区最上方的卡牌放置到废弃区直到我方生命卡牌变为 1 张。
///
/// 实现说明：
///   - 候选合并手牌 + 废弃区中费用≤7 且名称含“艾尼路”的角色卡。
///   - 选 1 张后按其来源（手牌/废弃）免费登场。
///   - 之后循环将我方生命顶放入废弃区直到剩 1 张。
///   - 【触发】另由触发系统处理，本脚本仅实现【主要】。
/// </summary>
public class EB01_060_IamGod : IScriptedEffect
{
    public string CardNumber => "EB01-060";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        bool Match(CardInstance c) =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 7 &&
            c.Info.NameContains("艾尼路");

        var handCands = me.Hand.Where(Match).ToList();
        var trashCands = me.Trash.Where(Match).ToList();
        var all = new List<CardInstance>();
        all.AddRange(handCands);
        all.AddRange(trashCands);

        if (all.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = all.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PlayEnel",
                "将手牌或废弃区中最多 1 张费用≤7 的“艾尼路”登场",
                all.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = all.First(c => c.Id.ToString() == chosen[0]);
                if (me.Hand.Contains(picked))
                    AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
                else
                    AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
            }
        }

        // 之后：将我方生命顶放入废弃区，直到生命变为 1 张
        while (me.LifeArea.Count > 1)
        {
            var top = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            me.Trash.Add(top);
        }
    }
}
