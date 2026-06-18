using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-116 人造恶魔果实SMILE（事件 / 暗 / 百兽海盗团・SMILE，cost2）
/// 【主要】确认我方卡组最上方的5张卡牌，将其中最多1张费用不高于3且拥有《SMILE》特征的角色卡牌登场。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明：
///   - EventMain；确认卡组顶5张，公开（让玩家选）最多1张费用≤3且拥有《SMILE》特征的角色，
///     从卡组直接登场（手动 Deck.Remove → Characters.Add）。
///   - "自选顺序放回卡组最下方"实现为保持原相对顺序放底。
/// </summary>
public class OP01_116_SMILE : IScriptedEffect
{
    public string CardNumber => "OP01-116";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int peek = Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var cand = top.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 3 &&
            c.Info.HasKeyword("SMILE")).ToList();

        if (cand.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "将最多1张费用≤3且拥有《SMILE》特征的角色登场",
                cand.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cand.First(c => c.Id.ToString() == chosen[0]);
                me.Deck.Remove(picked);
                if (me.Characters.Count >= 5)
                {
                    var sacrifice = me.Characters[0];
                    me.Characters.RemoveAt(0);
                    me.Trash.Add(sacrifice);
                }
                picked.TurnPlayed = ctx.State.TurnCount;
                me.Characters.Add(picked);
            }
        }

        // 之后：剩余按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
