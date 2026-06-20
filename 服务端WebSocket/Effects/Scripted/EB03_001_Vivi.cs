using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-001 奈菲特·薇薇（领航 / 炎・水 / 阿拉巴斯坦王国）
/// 【每回合1次】我方原本费用4或更高的角色将要被KO的场合，可以改为丢弃我方1张手牌，使该角色不会被KO。
/// 【启动主要】可以将此领袖转为休息状态：本回合中，对方最多1张角色力量-2000。
///   之后，本回合中，我方最多1张没有【攻击时】效果的角色获得【速攻】。
///
/// 实现说明：
///   - KO守护用 OnAllyWillBeKOd（仅战斗KO派发）。条件按「原本费用」c.Info.Cost>=4 判定；
///     每回合1次；置换成本为丢弃1张手牌，调用 MarkPreventKO 取消本次KO。
///   - 启动主要：成本为横置自身领袖（RestCard）；对方1张角色 -2000；
///     再给我方1张「没有【攻击时】效果」(EffectTags 不含 OnAttackDeclare) 的角色本回合【速攻】。
/// </summary>
public class EB03_001_Vivi : IScriptedEffect
{
    public string CardNumber => "EB03-001";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnAllyWillBeKOd || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnAllyWillBeKOd)
        {
            var key = self.Info.Number + "-guard" + ":" + self.Id;
            if (me.TurnOnceUsed.Contains(key)) return;

            var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
            if (victimId is null) return;
            var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
            if (victim is null) return;

            // 原本费用4或更高
            if (victim.Info.Cost < 4) return;

            // 需有手牌可弃作为置换成本
            if (me.Hand.Count == 0) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "薇薇【每回合1次】：丢弃我方1张手牌，使该角色不会被KO？");
            if (!use) return;

            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "丢弃1张手牌作为置换成本",
                me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (pick.Count == 0) return;
            var disc = me.Hand.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.DiscardHand(me, disc);

            ctx.State.MarkPreventKO(victim.Id);
            me.TurnOnceUsed.Add(key);
            return;
        }

        if (ctx.Trigger == EffectTrigger.ActivatedMain)
        {
            // 成本：横置此领袖
            AtomicOps.RestCard(self);

            // 对方最多1张角色力量-2000
            if (opp.Characters.Count > 0)
            {
                var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                    "选择对方最多1张角色，本回合力量-2000",
                    opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
                if (ch.Count > 0)
                {
                    var tgt = opp.Characters.First(c => c.Id.ToString() == ch[0]);
                    AtomicOps.AddPowerThisTurn(tgt, -2000);
                }
            }

            // 我方最多1张「没有【攻击时】效果」的角色获得【速攻】
            var cands = me.Characters
                .Where(c => !c.Info.EffectTags.Contains("OnAttackDeclare"))
                .ToList();
            if (cands.Count > 0)
            {
                var ch2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                    "选择我方最多1张没有【攻击时】效果的角色，本回合获得【速攻】",
                    cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
                if (ch2.Count > 0)
                {
                    var tgt = cands.First(c => c.Id.ToString() == ch2[0]);
                    AtomicOps.GiveKeyword(tgt, "速攻", KeywordDuration.ThisTurn);
                }
            }
            return;
        }
    }
}
