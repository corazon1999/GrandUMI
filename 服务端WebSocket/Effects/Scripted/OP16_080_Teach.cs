using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-080 马歇尔·D·提奇（领航）
/// 持续：【对方的回合中】我方所有角色费用 +1（永续效果，需 ContinuousEffect 注册）
/// 【对方的攻击时】每回合 1 次：丢弃 1 张带【触发】的手牌 → 攻击对象变为此领袖或我方《黑胡子海盗团》角色
/// 当前实现：仅启动主要部分（攻击重定向需 BattleEngine 扩展）
/// </summary>
public class OP16_080_Teach : IScriptedEffect
{
    public string CardNumber => "OP16-080";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        const string key = "OP16-080-OncePerTurn";
        if (me.TurnOnceUsed.Contains(key)) return;

        var triggerCards = me.Hand.Where(c => !string.IsNullOrEmpty(c.Info.Trigger)).ToList();
        if (triggerCards.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "提奇：弃 1 张带【触发】手牌，把攻击对象变为本领袖或《黑胡子海盗团》角色？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandWithTrigger",
            "选 1 张带【触发】的手牌丢弃",
            triggerCards.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;
        var discard = triggerCards.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.DiscardHand(me, discard);

        // 攻击对象重定向需 BattleEngine 扩展；当前占位仅消耗弃牌
        me.TurnOnceUsed.Add(key);
    }
}
