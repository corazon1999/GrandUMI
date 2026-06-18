using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-022 风车村（舞台）
/// 【启动主要】可以将此舞台转为休息状态：
///   本回合中，我方最多 1 张原本的力量不高于 2000 的角色力量 +1000。
///
/// 实现说明 / 简化点：
///   - 发动成本为"将此舞台转为休息状态"（此卡须处于活跃状态）。
///   - "原本的力量不高于 2000"取卡面原始力量 c.Info.Power 判定。
///   - "可以…"为可选，且"最多 1 张"故 min=0；无合法目标或玩家取消则不支付成本。
/// </summary>
public class OP13_022_Windmill : IScriptedEffect
{
    public string CardNumber => "OP13-022";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 成本前置检查：此舞台须处于活跃状态
        if (ctx.Source.IsTapped) return;

        // 候选：我方原本力量 ≤2000 的角色
        var candidates = me.Characters.Where(c => c.Info.Power <= 2000).ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将此舞台转为休息状态：选择我方最多 1 张原本力量不高于 2000 的角色，本回合力量 +1000",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        // 支付成本：将此舞台转为休息状态
        ctx.Source.IsTapped = true;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddPowerThisTurn(target, 1000);
    }
}
