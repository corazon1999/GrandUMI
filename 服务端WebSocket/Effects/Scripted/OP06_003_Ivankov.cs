using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-003 安普里奥·伊万科夫（角色 / 炎 5 费 6000，革命军）
/// 【登场时】确认我方卡组最上方的 3 张卡牌，将其中最多 1 张力量不高于 5000 且拥有《革命军》特征的
///   角色卡牌登场。之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明 / 简化点：
///   - 确认卡组顶 3 张：用 prompt 的 extra.choiceCards 展示卡面（卡组牌默认不下发身份）。
///   - "从卡组顶直接登场角色"：DSL 的 LookTopReveal 只能加入手牌，无从卡组顶登场角色的 op，故用脚本
///     直接操作 State（与引擎 PlayFromHandFree 的角色登场逻辑一致：满 5 时弃最左一张后登场）。
///   - "自选顺序放回卡组最下方"实现为保持原相对顺序放底（对实战影响极小）。
/// </summary>
public class OP06_003_Ivankov : IScriptedEffect
{
    public string CardNumber => "OP06-003";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int peek = Math.Min(3, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        // 候选：力量≤5000 且拥有《革命军》特征的角色
        var cands = top.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Power <= 5000 &&
            c.Info.HasKeyword("革命军")).ToList();

        if (cands.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "确认卡组顶 3 张，将其中最多 1 张力量≤5000 且《革命军》角色登场",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cands.First(c => c.Id.ToString() == chosen[0]);
                me.Deck.Remove(picked);
                // 从卡组顶登场角色（与 PlayFromHandFree 角色登场逻辑一致）
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

        // 之后：将剩余仍在顶部的卡牌按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
