using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-052 维奥拉（角色 / 光 / 德莱斯罗兹，cost2 power0）
/// 【阻挡者】（关键词，引擎处理）
/// 【登场时】选择以下的 1 项：
///   ・确认对方所有的生命卡牌，将其自选顺序放置。
///   ・将我方所有的生命卡牌翻至正面朝下。
///
/// 实现说明（M2 生命牌朝向 / 重排）：
///   - 二选一用 ChooseOption。
///   - 选项1：经 extra.choiceCards 把对方所有生命牌番号向我公开（"确认"），让我自选顺序后 ReorderLife 重排。
///   - 选项2：FlipAllLifeFaceDown 将我方所有生命翻至背面。
/// </summary>
public class EB01_052_Viola : IScriptedEffect
{
    public string CardNumber => "EB01-052";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "维奥拉【登场时】：选择 1 项",
            new[] { "确认对方所有生命牌并自选顺序放置", "将我方所有生命牌翻至正面朝下" });

        if (opt == 0)
        {
            if (opp.LifeArea.Count == 0) return;
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = opp.LifeArea.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            // 自选顺序：按点选先后形成顶→底顺序（全选）
            var ordered = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ViolaLifeReorder",
                "确认对方所有生命牌，自选顺序放置（先选者置于生命区最上方）",
                opp.LifeArea.Select(c => c.Id.ToString()).ToList(),
                opp.LifeArea.Count, opp.LifeArea.Count, extra);
            var order = ordered
                .Select(id => opp.LifeArea.FirstOrDefault(c => c.Id.ToString() == id))
                .Where(c => c is not null).Select(c => c!.Id).ToList();
            AtomicOps.ReorderLife(opp, order);
        }
        else
        {
            AtomicOps.FlipAllLifeFaceDown(me);
        }
    }
}
