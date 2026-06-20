using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-099 爱莎（角色）
/// 【登场时】确认我方或对方生命区最上方的最多1张卡牌，将其放置到生命区最上方或最下方。
///
/// 实现说明 / 简化点：
///   - 先让玩家二选一：确认"我方"还是"对方"的生命区顶卡（任一方若无生命牌则视为无效）。
///   - 取该方生命区最上方卡（LifeArea[0]），通过 choiceCards 下发其卡面供确认。
///   - 再让玩家二选一：放回该方生命区最上方或最下方。
///   - "确认"=查看一张牌后选择放顶/放底，本实现按此处理（卡牌仍属同一方生命区）。
/// </summary>
public class OP06_099_Aisa : IScriptedEffect
{
    public string CardNumber => "OP06-099";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 选择确认哪一方的生命区顶卡
        var sideOptions = new List<string>();
        var sideIsMine = new List<bool>();
        if (me.LifeArea.Count > 0) { sideOptions.Add("我方生命区最上方"); sideIsMine.Add(true); }
        if (opp.LifeArea.Count > 0) { sideOptions.Add("对方生命区最上方"); sideIsMine.Add(false); }
        if (sideOptions.Count == 0) return;

        int sidePick = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "确认哪一方生命区最上方的1张卡牌？", sideOptions);
        if (sidePick < 0 || sidePick >= sideOptions.Count) return;

        var target = sideIsMine[sidePick] ? me : opp;
        if (target.LifeArea.Count == 0) return;
        var topCard = target.LifeArea[0];

        // 下发被确认的卡面（生命牌默认不公开身份）
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = new List<object>
            {
                new { id = topCard.Id.ToString(), number = topCard.Info.Number },
            },
        };
        await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LifePeek",
            "确认该生命卡牌",
            new List<string> { topCard.Id.ToString() }, 1, 1, extra);

        // 放置到生命区最上方或最下方
        int placePick = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将其放置到生命区最上方还是最下方？",
            new List<string> { "最上方", "最下方" });

        target.LifeArea.Remove(topCard);
        if (placePick == 1) target.LifeArea.Add(topCard);
        else target.LifeArea.Insert(0, topCard);
    }
}
