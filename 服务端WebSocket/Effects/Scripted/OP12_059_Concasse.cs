using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-059 粗碎（事件）
/// 【主要】我方领袖为"山智"的场合，抽取 1 张卡牌。
/// 【反击】我方废弃区中有 4 张或更多事件的场合，本次战斗中，我方最多 1 张领袖力量 +4000。
///
/// 备注：【反击】的对象限定为"领袖"，候选只有我方领袖一张，故直接对领袖加力（玩家可选发动）。
/// </summary>
public class OP12_059_Concasse : IScriptedEffect
{
    public string CardNumber => "OP12-059";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            // 我方领袖为"山智" → 抽 1
            if (me.Leader.MatchesName("山智"))
                AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        // ── 【反击】 ──
        // 废弃区事件数 ≥ 4
        int events = me.Trash.Count(c => c.Info.Kind == CardKind.Event);
        if (events < 4) return;

        // "最多 1 张领袖" → 让玩家确认是否对领袖加力
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "粗碎【反击】：本次战斗中，我方领袖力量 +4000？");
        if (!use) return;

        AtomicOps.AddPowerThisBattle(me.Leader, 4000);
    }
}
