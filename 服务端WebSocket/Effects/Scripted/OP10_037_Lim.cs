using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-037 莉姆（角色）
/// 1. 【每回合1次】此角色因对方的效果将要离开场上的场合，可以改为将我方 1 张拥有《时光旅诗》
///    特征的角色转为休息状态，使此角色不离场。（替换效果）
/// 2. 【我方的回合结束时】将我方最多 1 张拥有《时光旅诗》特征的角色转为活跃状态。
///
/// 实现说明 / 简化点：
///   - 第 1 条用 PreKO 钩子近似实现：引擎当前仅向 PreKO 暴露"将要被 KO"，无法覆盖"退回手牌/
///     放回卡组"等其他离场，故仅拦截 KO（实战中"离开场上"绝大多数即 KO）。
///   - 替换成本为"将我方 1 张《时光旅诗》角色（可为自身以外）转休息"，无可休息目标则不替换。
///   - 每回合 1 次用 TurnOnceUsed 记录。
/// </summary>
public class OP10_037_Lim : IScriptedEffect
{
    public string CardNumber => "OP10-037";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.PreKO || t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.PreKO)
        {
            // 仅"因对方的效果"近似：非战斗结算中
            if (ctx.State.CurrentBattle is not null) return;

            var key = self.Info.Number + "-prevent" + ":" + self.Id;
            if (me.TurnOnceUsed.Contains(key)) return;

            // 可休息的《时光旅诗》角色（含自身以外的活跃角色）
            var cands = me.Characters
                .Where(c => c.Id != self.Id && !c.IsTapped && c.Info.HasKeyword("时光旅诗"))
                .ToList();
            if (cands.Count == 0) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "莉姆：将我方 1 张《时光旅诗》角色转为休息，改为使此角色不离场？");
            if (!use) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                "选择 1 张《时光旅诗》角色转为休息状态（替换莉姆离场）",
                cands.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (chosen.Count == 0) return;

            var costChar = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(costChar);
            ctx.State.MarkPreventKO(self.Id);
            me.TurnOnceUsed.Add(key);
            return;
        }

        // 【我方的回合结束时】将我方最多 1 张《时光旅诗》休息角色转为活跃
        var restCands = me.Characters
            .Where(c => c.IsTapped && c.Info.HasKeyword("时光旅诗"))
            .ToList();
        if (restCands.Count == 0) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方最多 1 张《时光旅诗》角色转为活跃状态",
            restCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count == 0) return;

        var target = restCands.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.ActivateCard(target);
    }
}
