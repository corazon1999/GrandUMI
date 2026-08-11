using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-061 Mr.2·盆·岁末(本萨姆)（角色 / 暗 / 原巴洛克工作室，cost4 power1000）
/// 【登场时】从咚!!卡组中追加最多 1 张活跃状态的咚!!。
/// 【攻击时】选择对方最多 1 张角色。本回合中，此角色原本的力量变为与被选中的角色相同。
///
/// 实现说明：
///   - 【登场时】用 AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active) 追加 1 张活跃咚!!。
///   - 【攻击时】"原本力量变为与被选中角色相同" → 复制被选中角色结算时的当前力量，
///     并写入 OriginalPowerOverride；本卡自身的咚与其他力量修正仍会叠加在新的原本力量上。
/// </summary>
public class EB01_061_Bentham : IScriptedEffect
{
    public string CardNumber => "EB01-061";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 从咚!!卡组追加最多 1 张活跃状态的咚!!
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
            return;
        }

        if (ctx.Trigger == EffectTrigger.OnAttackDeclare)
        {
            var cands = opp.Characters.ToList();
            if (cands.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 1 张角色：本回合此角色原本力量变为与其相同",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count == 0) return;

            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            // 本回合此角色原本力量变为被选角色结算时的当前力量
            self.OriginalPowerOverride = ctx.State.CurrentPowerOf(tgt);
            return;
        }
    }
}
