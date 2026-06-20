using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-104 夏莉（角色）
/// 【阻挡者】（由引擎以关键词处理）
/// 【登场时】确认我方或对方生命区最上方的最多 1 张卡牌，放置到生命区最上方或最下方。
///
/// 说明 / 简化点：
///   - 仅实现【登场时】主动效果；【阻挡者】为关键词，引擎自动处理。
///   - 生命区为非公开区，向操作者展示卡面通过 extra.choiceCards 下发。
///   - 对单张顶牌：放顶=保持原位，放底=移到末尾。
/// </summary>
public class OP03_104_Shirley : IScriptedEffect
{
    public string CardNumber => "OP03-104";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var sideOptions = new List<string>();
        if (me.LifeArea.Count > 0)  sideOptions.Add("我方生命区最上方");
        if (opp.LifeArea.Count > 0) sideOptions.Add("对方生命区最上方");
        if (sideOptions.Count == 0) return;

        int sidePick = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "确认哪一方生命区最上方的卡牌？", sideOptions);
        var targetLife = sideOptions[sidePick].StartsWith("我方") ? me.LifeArea : opp.LifeArea;
        if (targetLife.Count == 0) return;

        var topCard = targetLife[0];
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = new[] { new { id = topCard.Id.ToString(), number = topCard.Info.Number } }.ToList(),
        };
        _ = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PeekLife",
            "确认生命区最上方的卡牌", new[] { topCard.Id.ToString() }.ToList(), 0, 0, extra);

        int place = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将该卡牌放置到？", new List<string> { "生命区最上方", "生命区最下方" });
        if (place == 1)
        {
            targetLife.RemoveAt(0);
            targetLife.Add(topCard);
        }
    }
}
