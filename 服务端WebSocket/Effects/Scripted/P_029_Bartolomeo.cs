using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-029 巴尔托洛梅奥（角色 / 风 / FILM·超新星·巴尔托俱乐部，cost2 power3000）
/// 【我方的回合结束时】可以将此角色转为休息状态：
///   将我方最多 1 张"巴尔托洛梅奥"以外的、拥有《FILM》特征的角色转为活跃状态。
///
/// 实现说明：
///   - 成本"横置自身"为可选：先 ConfirmOptional；本卡已是休息状态则无法支付成本。
///   - 收益：选我方最多 1 张名称非"巴尔托洛梅奥"且拥有《FILM》特征的角色转为活跃。
/// </summary>
public class P_029_Bartolomeo : IScriptedEffect
{
    public string CardNumber => "P-029";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (self.IsTapped) return; // 已横置，无法支付成本

        var targets = me.Characters
            .Where(c => c.Id != self.Id
                        && c.IsTapped
                        && !c.MatchesName("巴尔托洛梅奥")
                        && c.Info.HasKeyword("FILM"))
            .ToList();
        if (targets.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "巴尔托洛梅奥【我方的回合结束时】：横置自身，将最多 1 张《FILM》角色转为活跃状态？");
        if (!use) return;

        // 成本：横置自身
        AtomicOps.RestCard(self);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方最多 1 张《FILM》角色转为活跃状态",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.ActivateCard(tgt);
        }
    }
}
