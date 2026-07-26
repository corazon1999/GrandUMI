using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-070 山智（黑/暗）
/// 效果原文：
///   1. 我方废弃区中每有 5 张事件，此角色的力量 +1000。（永续）
///   2. 此角色因对方的效果将要离开场上的场合，可以改为将我方场上的 1 张咚!! 放回咚!!卡组，
///      使此角色不离场。（替换效果）
///
/// 实现说明：
///   - 第 1 条永续力量加成：在【登场时】向 GameState.ContinuousEffects 注册若干个固定 +1000
///     的永续效果（按事件张数阈值 5/10/15/... 分层），Predicate 内实时统计我方废弃区中的
///     事件数量，从而随废弃区变化自动增减加成；Filter 限定仅作用于此角色自身。
///     来源卡离场后由 TurnEngine 自动清理这些永续效果。注册具幂等性（避免重复登场/再触发叠加）。
///   - 第2条覆盖效果KO与非KO离场，以咚!!-1作为替代成本并阻止对应离场。
/// </summary>
public class OP12_070_Sanji : IScriptedEffect
{
    public string CardNumber => "OP12-070";

    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.OnEnterField or EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField;

    // 支持的最高层数（5*8 = 40 张事件足够覆盖任何实战废弃区规模）
    private const int MaxTiers = 8;

    public async Task Resolve(EffectContext ctx)
    {
        var state = ctx.State;
        var self = ctx.Source;
        var selfId = self.Id;
        int ownerIdx = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 幂等：先移除本卡已注册过的同源永续效果，避免重复登场/重复触发造成叠加
            state.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

            for (int tier = 1; tier <= MaxTiers; tier++)
            {
                int threshold = tier * 5;
                state.ContinuousEffects.Add(new ContinuousEffect
                {
                    SourceCardId = selfId.ToString(),
                    PowerDelta = 1000,
                    Scope = new ContinuousScope
                    {
                        Side = 0,
                        IncludeLeader = false,
                        IncludeCharacters = true,
                        Filter = c => c.Id == selfId,
                    },
                    Predicate = (st, sideIdx, card) =>
                    {
                        if (card.Id != selfId) return false;
                        var owner = st.Players[ownerIdx];
                        int eventCount = owner.Trash.Count(c => c.Info.Kind == CardKind.Event);
                        return eventCount >= threshold;
                    },
                });
            }
            return;
        }

        bool nonKoLeave = ctx.Trigger == EffectTrigger.OnAllyWillLeaveField;
        if (!nonKoLeave &&
            (state.KOReason != "effect" || state.KOActingSide != 1 - ctx.OwnerIndex)) return;
        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        var victimOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int oi ? oi : -1;
        if (victimOwner != ctx.OwnerIndex || victimId != selfId.ToString()) return;

        var me = state.Players[ctx.OwnerIndex];
        if (me.CostArea.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "山智：支付咚!!-1，使此角色不离场？")) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        if (nonKoLeave) state.MarkPreventLeave(selfId);
        else state.MarkPreventKO(selfId);
    }
}
