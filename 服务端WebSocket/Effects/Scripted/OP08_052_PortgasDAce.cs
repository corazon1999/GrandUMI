using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-052 波特夹斯·D·艾斯（角色，水）
/// 【登场时】公开我方卡组最上方的 1 张卡牌，并将其中最多 1 张费用不高于 4 且拥有的特征中
///   包含 <白胡子海盗团> 的角色卡牌登场。之后，将剩余的卡牌放回卡组最上方或最下方。
///
/// 实现说明 / 简化点：
///   - 公开顶牌通过 prompt 的 extra.choiceCards 下发卡面（卡组牌默认不下发身份）。
///   - 满足条件（角色 / 费用≤4 / 含《白胡子海盗团》）才可选择登场；登场即无剩余牌。
///   - 未登场时让玩家二选一将公开的牌放回卡组最上方或最下方。
/// </summary>
public class OP08_052_PortgasDAce : IScriptedEffect
{
    public string CardNumber => "OP08-052";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (me.Deck.Count == 0) return;
        var top = me.Deck[0];

        var revealExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = new[] { new { id = top.Id.ToString(), number = top.Info.Number } }.ToList(),
        };

        bool eligible = top.Info.Kind == CardKind.Character
            && top.Info.Cost <= 4
            && top.Info.HasKeyword("白胡子海盗团");

        if (eligible)
        {
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "公开卡组顶 1 张，可将该费用≤4 的《白胡子海盗团》角色登场",
                new List<string> { top.Id.ToString() }, 0, 1, revealExtra);
            if (pick.Count > 0)
            {
                me.Deck.Remove(top);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, top);
                return; // 已登场，无剩余牌
            }
        }

        // 未登场：将公开的卡牌放回卡组最上方或最下方
        int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将公开的卡牌放回卡组最上方还是最下方？", new[] { "最上方", "最下方" });
        if (where == 1)
        {
            me.Deck.Remove(top);
            me.Deck.Add(top);
        }
    }
}
