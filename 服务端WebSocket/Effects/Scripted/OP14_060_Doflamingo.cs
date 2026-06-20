using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-060 堂吉诃德·多弗拉门戈（领航）
/// 【对方的攻击时】【每回合1次】咚!!-1：选择我方领袖或 1 张拥有《堂吉诃德海盗团》特征的角色，
///   将攻击对象变更为被选中的卡牌。
///
/// 实现说明 / 简化点：
///   - 用 OnOppAttackDeclare 钩子；此时 ctx.State.CurrentBattle 已建立(我方为防守方)，
///     直接重定向其目标字段(12.3)。
///   - 成本"咚!!-1"= 将费用区 1 张咚!!放回咚!!卡组(ReturnDonToDeck)，仅在确认发动后支付。
///   - 每回合1次用 TurnOnceUsed。
/// </summary>
public class OP14_060_Doflamingo : IScriptedEffect
{
    public string CardNumber => "OP14-060";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var b = ctx.State.CurrentBattle;
        if (b is null) return;

        // 每回合1次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本咚!!-1 需要费用区至少 1 张咚!!
        if (me.TotalDonInCostArea < 1) return;

        // 候选：我方领袖 + 拥有《堂吉诃德海盗团》特征的角色
        var cands = new List<CardInstance> { me.Leader };
        cands.AddRange(me.Characters.Where(c => c.Info.HasKeyword("堂吉诃德海盗团")));
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "多弗拉门戈【对方的攻击时】：咚!!-1，将攻击对象变更为我方领袖或《堂吉诃德海盗团》角色？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择我方领袖或 1 张《堂吉诃德海盗团》角色，将攻击对象变更为它",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;

        // 支付成本：咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        me.TurnOnceUsed.Add(key);

        var target = cands.First(c => c.Id.ToString() == chosen[0]);
        if (target.Id == me.Leader.Id)
        {
            b.TargetIsLeader = true;
            b.TargetCardId = null;
        }
        else
        {
            b.TargetIsLeader = false;
            b.TargetCardId = target.Id;
        }
    }
}
