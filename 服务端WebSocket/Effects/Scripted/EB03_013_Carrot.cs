using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-013 凯罗特（角色 / 风 / 纯毛族）
/// 【启动主要】【每回合1次】在此角色登场的回合的场合，
///   将对方最多1张处于休息状态且费用不高于5的角色KO。之后，将我方手牌中最多1张“佐乌”登场。
///
/// 实现说明：
///   - 「在此角色登场的回合」= 自身 TurnPlayed == 当前 TurnCount。
///   - 每回合1次用 OncePerTurnUsedKeys / TurnOnceUsed。
///   - KO 目标：对方休息(IsTapped)且 Info.Cost<=5 的角色。
///   - 之后从手牌登场名为“佐乌”的角色（MatchesName / NameContains）。
/// </summary>
public class EB03_013_Carrot : IScriptedEffect
{
    public string CardNumber => "EB03-013";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 在此角色登场的回合
        if (self.TurnPlayed != ctx.State.TurnCount) return;

        // 每回合1次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);

        // KO 对方最多1张休息状态且费用≤5的角色
        var koCands = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 5).ToList();
        if (koCands.Count > 0)
        {
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多1张休息状态且费用≤5的角色KO",
                koCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0)
            {
                var tgt = koCands.First(c => c.Id.ToString() == ch[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }

        // 之后：将我方手牌中最多1张“佐乌”登场
        var zou = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.MatchesName("佐乌")).ToList();
        if (zou.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = zou.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var ch2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
                "将我方手牌中最多1张“佐乌”登场",
                zou.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (ch2.Count > 0)
            {
                var picked = zou.First(c => c.Id.ToString() == ch2[0]);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
            }
        }
    }
}
