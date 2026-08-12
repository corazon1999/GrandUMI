using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-061 堂吉诃德·罗西南德（领航 4 费 5000，海军/堂吉诃德海盗团）
/// 1. 【每回合1次】我方的"特拉法尔加·罗"将要被 KO 的场合，可以改为将我方生命区最上方的 1 张
///    卡牌加入手牌，使该"特拉法尔加·罗"不会被 KO。（替换/防 KO）
/// 2. 【启动主要】【每回合1次】咚!!-1：本回合中，我方下次从手牌中登场的费用为 4 或更高的
///    "特拉法尔加·罗"需支付的费用减少 2。
///
/// 实现说明：
///   - 能力 1 通过 OnAllyWillBeKOd 监听我方其它角色将被 KO：确认目标名称后，将生命顶加入手牌并标记本次 KO 无效。
///   - 用 OneShotPlayDiscount 注册"本回合下一次"从手牌登场、原本费用≥4 的"特拉法尔加·罗" -2 的
///     一次性减费（CardPlayer 打出该类卡时消费一次即移除；回合末 TurnEngine 统一清空）。
///     这精确对应原文"下次…一次"（反馈#135：旧用 ContinuousEffect 会让本回合所有罗都减费、用不完）。
/// </summary>
public class OP12_061_Rosinante : IScriptedEffect
{
    public string CardNumber => "OP12-061";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnAllyWillBeKOd || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnAllyWillBeKOd)
        {
            var guardKey = $"OP12-061-guard:{ctx.Source.Id}";
            if (me.TurnOnceUsed.Contains(guardKey) || me.LifeArea.Count == 0) return;

            var victimId = ctx.Vars.TryGetValue("victimId", out var value) ? value as string : null;
            var victim = me.Characters.FirstOrDefault(card => card.Id.ToString() == victimId);
            if (victim is null || !victim.MatchesName("特拉法尔加·罗")) return;

            if (!await ctx.Prompts.ConfirmOptional(owner,
                    "罗西南德【每回合1次】：将生命区最上方1张卡牌加入手牌，使该“特拉法尔加·罗”不会被KO？")) return;

            var lifeTop = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            lifeTop.IsLifeFaceUp = false;
            me.Hand.Add(lifeTop);
            ctx.State.MarkPreventKO(victim.Id);
            me.TurnOnceUsed.Add(guardKey);
            return;
        }

        var key = "OP12-061-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本：咚!!-1
        if (me.CostArea.Count < 1) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        me.TurnOnceUsed.Add(key);

        // 一次性减费：我方"本回合下一次"从手牌登场、原本费用≥4 的"特拉法尔加·罗" -2。
        // 打出一次即被 CardPlayer 消费、回合末 TurnEngine 清空，精确对应原文"下次…一次"。
        ctx.State.OneShotPlayDiscounts.RemoveAll(d => d.Owner == owner && d.NameContains == "特拉法尔加·罗"); // 防叠加
        ctx.State.OneShotPlayDiscounts.Add(new OneShotPlayDiscount
        {
            Owner = owner,
            Amount = 2,
            MinCost = 4,
            NameContains = "特拉法尔加·罗",
        });
    }
}
