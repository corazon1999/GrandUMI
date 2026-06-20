using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-049 凯罗特（角色 1 费 1000，纯毛族）
/// 【登场时】确认我方卡组最上方的 3 张卡牌，自选顺序排列并放置到卡组最上方或最下方。
/// 【对方的攻击时】可以将此角色放置到废弃区：本次战斗中，我方最多 1 张领袖力量 +1000。
///
/// 实现说明 / 简化点：
///   - 【登场时】：看卡组顶 3 张，让玩家选择整体放回"顶"或"底"。自选顺序简化为：
///     允许玩家逐张选择顺序（ChooseCards min=max=k 选出排列），用 ReorderTopK 放回顶/底。
///   - 【对方的攻击时】：成本为把此角色放入废弃区；收益是我方领袖本次战斗 +1000
///     （文本"最多 1 张领袖"在本卡语境即我方领袖）。
/// </summary>
public class OP11_049_Carrot : IScriptedEffect
{
    public string CardNumber => "OP11-049";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            int k = Math.Min(3, me.Deck.Count);
            if (k == 0) return;
            var top = me.Deck.Take(k).ToList();

            // 选择整体放回顶部或底部
            int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "凯罗特【登场时】：确认卡组顶 3 张，放回卡组最上方还是最下方？",
                new[] { "放回最上方", "放回最下方" });
            bool toBottom = where == 1;

            // 自选顺序：让玩家按希望的顺序逐一选择全部 k 张
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var order = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReorderTop",
                $"按希望的顺序选择 {k} 张卡牌（先选的在最前）",
                top.Select(c => c.Id.ToString()).ToList(), k, k, extra);

            List<Guid> orderGuids;
            if (order.Count == k)
                orderGuids = order.Select(Guid.Parse).ToList();
            else
                orderGuids = top.Select(c => c.Id).ToList(); // 未完整选择 → 保持原序

            AtomicOps.ReorderTopK(me, orderGuids, toBottom);
            return;
        }

        // OnOppAttackDeclare
        if (!me.Characters.Contains(self)) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "凯罗特【对方的攻击时】：将此角色放置到废弃区，本次战斗我方领袖力量 +1000？");
        if (!use) return;

        // 成本：将此角色放入废弃区
        me.Characters.Remove(self);
        me.Trash.Add(self);

        // 收益：我方领袖本次战斗 +1000
        AtomicOps.AddPowerThisBattle(me.Leader, 1000);
    }
}
