using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-008 凯文迪修（角色）
/// 【登场时】可以将我方的1张生命卡牌加入手牌：本回合中，此角色获得【速攻】效果。
///
/// 实现说明：
///   - 可选成本：将生命区最上方 1 张加入手牌（ConfirmOptional 询问）。
///   - 收益：本回合赋予自身【速攻】(GiveKeyword ThisTurn，引擎据此放行登场回合攻击)。
/// </summary>
public class OP01_008_Cavendish : IScriptedEffect
{
    public string CardNumber => "OP01-008";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (me.LifeArea.Count == 0) return; // 无生命牌 → 无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "凯文迪修【登场时】：将生命区最上方1张加入手牌，本回合此角色获得【速攻】？");
        if (!use) return;

        // 成本：生命区最上方 1 张加入手牌
        var top = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        me.Hand.Add(top);

        // 收益：本回合获得【速攻】
        AtomicOps.GiveKeyword(self, "速攻", KeywordDuration.ThisTurn);
    }
}
