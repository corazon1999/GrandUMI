using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-099 夏洛特·卡塔库栗（领航）
/// 【咚‼×1】【攻击时】确认我方或对方生命区最上方的最多 1 张卡牌，放置到生命区的最上方或最下方。
///   之后，本次战斗中，此领袖的力量+1000。
///
/// 说明 / 简化点：
///   - 【咚‼×1】的发动条件由引擎在触发该效果前校验，脚本只实现收益。
///   - "确认生命顶 1 张" 实现为先选我方/对方生命区，再选该生命区最上方那张放顶或放底。
///     （对单张顶牌而言放顶=保持原位、放底=移到末尾。）
///   - 生命区为非公开区，向操作者展示卡面通过 extra.choiceCards 下发。
/// </summary>
public class OP03_099_Katakuri : IScriptedEffect
{
    public string CardNumber => "OP03-099";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 选择查看哪一方的生命顶
        var sideOptions = new List<string>();
        if (me.LifeArea.Count > 0)  sideOptions.Add("我方生命区最上方");
        if (opp.LifeArea.Count > 0) sideOptions.Add("对方生命区最上方");

        if (sideOptions.Count > 0)
        {
            int sidePick = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "确认哪一方生命区最上方的卡牌？", sideOptions);
            var targetLife = sideOptions[sidePick].StartsWith("我方") ? me.LifeArea : opp.LifeArea;

            if (targetLife.Count > 0)
            {
                var topCard = targetLife[0];
                var extra = new Dictionary<string, object?>
                {
                    ["choiceCards"] = new[] { new { id = topCard.Id.ToString(), number = topCard.Info.Number } }.ToList(),
                };
                // 借 ChoiceCards 把卡面展示给操作者，再选放顶/放底
                _ = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PeekLife",
                    "确认生命区最上方的卡牌", new[] { topCard.Id.ToString() }.ToList(), 0, 0, extra);

                int place = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                    "将该卡牌放置到？", new List<string> { "生命区最上方", "生命区最下方" });
                if (place == 1)
                {
                    targetLife.RemoveAt(0);
                    targetLife.Add(topCard);
                }
                // place==0：保持原位（已在最上方）
            }
        }

        // 之后：本次战斗中此领袖力量+1000
        AtomicOps.AddPowerThisBattle(self, 1000);
    }
}
