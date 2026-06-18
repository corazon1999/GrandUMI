using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-049 斯比德·基尔（角色）
/// 【登场时】公开我方卡组最上方的 1 张卡牌，并将该卡牌放回卡组最上方或最下方。
///   公开的卡牌拥有的特征中包含〈白胡子海盗团〉的场合，本回合中此角色获得【速攻】效果。
///
/// 实现说明 / 简化点：
///   - 手动取卡组顶 1 张并通过 extra.choiceCards 公开其卡面（卡组牌默认不下发身份）。
///   - 用 ChooseOption 让玩家选择放回卡组最上方或最下方。
///   - 若公开的牌含〈白胡子海盗团〉，用 GiveKeyword 赋予自身本回合【速攻】。
/// </summary>
public class OP08_049_SpeedJiru : IScriptedEffect
{
    public string CardNumber => "OP08-049";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (me.Deck.Count == 0) return;
        var topCard = me.Deck[0];

        // 公开卡组顶 1 张，并选择放回最上方或最下方
        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            $"公开卡组顶：{topCard.Info.Number} —— 选择放回卡组最上方或最下方",
            new[] { "放回最上方", "放回最下方" });

        if (opt == 1)
        {
            me.Deck.Remove(topCard);
            me.Deck.Add(topCard);
        }
        // opt == 0 时保持在最上方，无需移动

        // 公开牌含〈白胡子海盗团〉→ 本回合自身获得【速攻】
        if (topCard.Info.HasKeyword("白胡子海盗团"))
        {
            AtomicOps.GiveKeyword(self, "速攻", KeywordDuration.ThisTurn);
        }
    }
}
