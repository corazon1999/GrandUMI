using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-007 托尼托尼·乔巴（角色）
/// 【我方的回合中】【登场时】/【攻击时】确认我方卡组最上方的5张卡牌，
///   将其中最多1张力量不高于4000且拥有《动物》特征的角色卡牌以休息状态登场。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明：
///   - 时机：OnEnterField（登场时）/ OnAttackDeclare（攻击时），二者均发生在我方回合，
///     额外校验 CurrentTurnPlayer == OwnerIndex 以满足【我方的回合中】。
///   - 看顶 5 张，公开候选（力量≤4000 且《动物》角色），玩家选最多 1 张以休息状态登场。
///   - 剩余牌按原相对顺序放回卡组最下方（自选顺序简化为原序）。
/// </summary>
public class OP08_007_Chopper : IScriptedEffect
{
    public string CardNumber => "OP08-007";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【我方的回合中】
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        int k = Math.Min(5, me.Deck.Count);
        if (k == 0) return;
        var top = me.Deck.Take(k).ToList();

        var cands = top.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Power <= 4000 &&
            c.Info.HasKeyword("动物")
        ).ToList();

        if (cands.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "确认卡组顶 5 张，将最多 1 张力量≤4000 的《动物》角色以休息状态登场",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cands.First(c => c.Id.ToString() == chosen[0]);
                me.Deck.Remove(picked);
                // 以休息状态登场（直接进入角色区；角色区满 5 张时牺牲最左侧）
                if (me.Characters.Count >= 5)
                {
                    var sacrifice = me.Characters[0];
                    me.Characters.RemoveAt(0);
                    me.Trash.Add(sacrifice);
                }
                picked.TurnPlayed = ctx.State.TurnCount;
                picked.IsTapped = true;
                me.Characters.Add(picked);
            }
        }

        // 剩余牌按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
