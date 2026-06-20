using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST13-002 波特夹斯·D·艾斯（领航）
/// 【咚!!×2】【启动主要】【每回合1次】确认我方卡组顶5张，将其中最多1张费用5的角色正面朝上加入生命区顶；之后剩余放回卡组底。
/// 【我方的回合结束时】将我方生命区中所有正面朝上的卡牌放置到废弃区。
/// </summary>
public class ST13_002_Ace : IScriptedEffect
{
    public string CardNumber => "ST13-002";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain || t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnMyTurnEnd)
        {
            foreach (var c in me.LifeArea.Where(c => c.IsLifeFaceUp).ToList()) { me.LifeArea.Remove(c); me.Trash.Add(c); }
            return;
        }
        // ActivatedMain
        if (me.AttachedDonCount(ctx.Source.Id) < 2) return;
        var key = "ST13-002-Activated" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);
        int peek = System.Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();
        var cands = top.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost == 5).ToList();
        if (cands.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "确认卡组顶5张，将其中最多1张费用5角色正面朝上加入生命区顶",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (ch.Count > 0)
            {
                var picked = cands.First(c => c.Id.ToString() == ch[0]);
                me.Deck.Remove(picked);
                picked.IsLifeFaceUp = true;
                me.LifeArea.Insert(0, picked);
                ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new List<string> { picked.Info.Number });
            }
        }
        // 剩余仍在顶部的牌放回卡组底（保持原相对顺序）
        var rest = me.Deck.Take(peek).Where(c => top.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}

/// <summary>
/// ST13-003 蒙奇·D·路飞（领航）
/// 【咚!!×2】【启动主要】【每回合1次】可以丢弃我方1张手牌：我方生命为0张时，将手牌或废弃区中最多2张费用5的角色正面朝上加入生命区顶。
/// 实现说明：原文“规则上 正面朝上生命入手改为放回卡组底”需 LifeRevealManager 规则钩子，暂未实现；此处实现启动主要部分。
/// </summary>
public class ST13_003_Luffy : IScriptedEffect
{
    public string CardNumber => "ST13-003";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.AttachedDonCount(ctx.Source.Id) < 2) return;
        var key = "ST13-003-Activated" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        if (me.Hand.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "丢弃1张手牌，发动路飞的【启动主要】？")) return;
        var dch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand", "丢弃1张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (dch.Count == 0) return;
        AtomicOps.DiscardHand(me, me.Hand.First(c => c.Id.ToString() == dch[0]));
        me.TurnOnceUsed.Add(key);
        if (me.LifeCount != 0) return;
        // 手牌或废弃区费用5角色，最多2张，正面朝上加入生命顶
        for (int i = 0; i < 2; i++)
        {
            var cands = me.Hand.Concat(me.Trash).Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost == 5).ToList();
            if (cands.Count == 0) break;
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandOrTrash",
                $"将手牌或废弃区中1张费用5角色正面朝上加入生命区顶（{i + 1}/2，可放弃）",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (ch.Count == 0) break;
            var picked = cands.First(c => c.Id.ToString() == ch[0]);
            me.Hand.Remove(picked); me.Trash.Remove(picked);
            picked.IsLifeFaceUp = true;
            me.LifeArea.Insert(0, picked);
        }
    }
}

/// <summary>
/// ST26-005 蒙奇·D·路飞（角色）
/// 【登场时】/【攻击时】咚!!-2：我方领袖为多种颜色且对方场上咚!!≥5张时，我方《草帽一伙》领袖原本力量变为7000。
/// （原本力量覆写直到本回合结束清除，近似“直到下个对方回合结束”）
/// </summary>
public class ST26_005_Luffy : IScriptedEffect
{
    public string CardNumber => "ST26-005";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        bool multiColor = me.Leader.Info.ColorList.Length >= 2;
        if (!(multiColor && opp.TotalDonInCostArea >= 5 && me.Leader.Info.HasKeyword("草帽一伙"))) return;
        if (me.CostArea.Count < 2) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 2)) return;
        me.Leader.OriginalPowerOverride = 7000;
    }
}
