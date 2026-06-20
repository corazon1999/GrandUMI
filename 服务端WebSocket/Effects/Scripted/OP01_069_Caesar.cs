using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-069 凯撒·库朗（角色 / 水）
/// 【KO时】从我方的卡组中登场最多1张"史姆丽"，并切洗卡组。
///
/// 实现说明：
///   - 【KO时】= OnKO 时机。从卡组找名为"史姆丽"的角色，让玩家选最多1张以活跃状态登场。
///   - 卡组牌默认不下发身份，prompt 传 extra.choiceCards 展示候选卡面。
///   - 登场后切洗卡组（打乱牌序）。场上角色已满5张则不再登场（无空位）。
/// </summary>
public class OP01_069_Caesar : IScriptedEffect
{
    public string CardNumber => "OP01-069";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (me.Characters.Count >= 5) return;

        var cands = me.Deck.Where(c =>
            c.Info.Kind == CardKind.Character && c.MatchesName("史姆丽")).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DeckCharacter",
            "从卡组登场最多1张《史姆丽》",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(c => c.Id.ToString() == chosen[0]);
            me.Deck.Remove(picked);
            picked.TurnPlayed = ctx.State.TurnCount;
            picked.IsTapped = false;
            me.Characters.Add(picked);
        }

        // 切洗卡组
        var rng = new Random();
        for (int i = me.Deck.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (me.Deck[i], me.Deck[j]) = (me.Deck[j], me.Deck[i]);
        }
    }
}
