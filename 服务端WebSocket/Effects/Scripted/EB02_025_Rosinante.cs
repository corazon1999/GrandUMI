using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-025 堂吉诃德·罗西南德（角色 / 水 / 海军·堂吉诃德海盗团，cost2 power3000）
/// 【启动主要】可以将我方的1张咚!!和此角色转为休息状态：我方领袖为"堂吉诃德·罗西南德"的场合，
///   确认我方卡组最上方的5张卡牌，将最多1张费用不高于2的角色卡牌以休息状态登场。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明：
///   - 仅在领袖为"堂吉诃德·罗西南德"时本效果有收益，故仅在该前提下询问发动。
///   - 成本：横置我方1张活跃咚!!（用 ReturnDonToDeck 不对，应是转为休息——直接将1张活跃咚置为休息状态）+ 横置此角色。
///   - 确认卡组顶5张，公开（让玩家选）最多1张费用≤2角色，从卡组以休息状态直接登场（手动 Deck.Remove → Characters.Add，IsTapped=true）。
///   - 剩余按原相对顺序放回卡组最下方。
/// </summary>
public class EB02_025_Rosinante : IScriptedEffect
{
    public string CardNumber => "EB02-025";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 仅领袖为"堂吉诃德·罗西南德"时有意义
        if (!me.Leader.Info.NameContains("堂吉诃德·罗西南德")) return;

        // 成本需求：至少1张活跃咚!!，且此角色为活跃
        if (me.ActiveDonCount < 1) return;
        if (ctx.Source.IsTapped) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "罗西南德【启动主要】：横置1张咚!!与此角色，确认卡组顶5张并以休息状态登场最多1张费用≤2角色？");
        if (!use) return;

        // 成本：将1张活跃咚!!转为休息状态
        var activeDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (activeDon is null) return;
        activeDon.State = DonState.Rest;

        // 成本：将此角色转为休息状态
        AtomicOps.RestCard(ctx.Source);

        // 效果：确认卡组顶5张
        int peek = Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var cand = top.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 2).ToList();
        if (cand.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "以休息状态登场最多1张费用≤2的角色",
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
                picked.IsTapped = true; // 以休息状态登场
                me.Characters.Add(picked);
            }
        }

        // 之后：剩余按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
