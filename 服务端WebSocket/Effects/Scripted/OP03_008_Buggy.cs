using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-008 巴奇（角色 / 炎 1 费 3000，巴奇海盗团）
/// 此角色在与拥有属性（斩）的卡牌的战斗中不会被KO。
/// 【登场时】确认我方卡组最上方的5张卡牌，公开其中最多1张红色事件并加入手牌。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明 / 简化点：
///   - 【登场时】检索：确认顶5张，公开其中最多1张「红色（炎）事件」加入手牌，其余按原相对顺序放底。
///   - "与属性（斩）卡牌战斗中不会被KO" 为依赖「当前战斗对手属性」的条件防KO，KoGuard 谓词无法获知
///     战斗对手的属性，引擎无对应通道，故此条静态防KO未实现（仅实现登场检索），属可接受简化。
/// </summary>
public class OP03_008_Buggy : IScriptedEffect
{
    public string CardNumber => "OP03-008";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int k = Math.Min(5, me.Deck.Count);
        if (k == 0) return;
        var top = me.Deck.Take(k).ToList();

        var cand = top.Where(c =>
            c.Info.Kind == CardKind.Event && c.Info.ColorList.Contains("红")).ToList();
        if (cand.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "确认卡组顶5张，公开最多1张红色事件加入手牌",
                cand.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (ch.Count > 0)
            {
                var p = cand.First(c => c.Id.ToString() == ch[0]);
                me.Deck.Remove(p);
                me.Hand.Add(p);
            }
        }

        // 其余按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
