using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-022 布鲁克（领航）
/// 启动主要每回合 1 次：将卡组顶 4 张放废弃区
/// 持续规则：卡组 0 张不立即败北，但变为 0 的回合结束时败北（特殊判定，需引擎扩展）
/// </summary>
public class OP15_022_Brook : IScriptedEffect
{
    public string CardNumber => "OP15-022";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var key = "OP15-022-MainOncePerTurn" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        AtomicOps.MillTop(me, 4);
        me.TurnOnceUsed.Add(key);
        if (me.Deck.Count != 0 || me.Characters.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "卡组为0张，将我方最多1张角色转为活跃状态",
            me.Characters.Select(card => card.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var target = me.Characters.FirstOrDefault(card => card.Id.ToString() == chosen[0]);
            if (target is not null) AtomicOps.ActivateCard(target);
        }
    }
}
