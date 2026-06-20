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
/// 实现说明 / 简化点：
///   - 第 1 条永续力量加成：在【登场时】向 GameState.ContinuousEffects 注册若干个固定 +1000
///     的永续效果（按事件张数阈值 5/10/15/... 分层），Predicate 内实时统计我方废弃区中的
///     事件数量，从而随废弃区变化自动增减加成；Filter 限定仅作用于此角色自身。
///     来源卡离场后由 TurnEngine 自动清理这些永续效果。注册具幂等性（避免重复登场/再触发叠加）。
///   - 第 2 条"替换离场"为引擎替换钩子缺口（PreKO 仅能取消 KO，无法拦截所有"离开场上"，
///     也无"将咚放回咚卡组作为代价"的交互），本脚本未实现该替换效果。
/// </summary>
public class OP12_070_Sanji : IScriptedEffect
{
    public string CardNumber => "OP12-070";

    // 永续力量加成在登场时一次性注册
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    // 支持的最高层数（5*8 = 40 张事件足够覆盖任何实战废弃区规模）
    private const int MaxTiers = 8;

    public Task Resolve(EffectContext ctx)
    {
        var state = ctx.State;
        var selfId = ctx.Source.Id;
        int ownerIdx = ctx.OwnerIndex;

        // 幂等：先移除本卡已注册过的同源永续效果，避免重复登场/重复触发造成叠加
        state.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        for (int tier = 1; tier <= MaxTiers; tier++)
        {
            int threshold = tier * 5; // 第 tier 层需要废弃区有 >= threshold 张事件
            state.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                PowerDelta = 1000,
                Scope = new ContinuousScope
                {
                    Side = 0,                 // 仅我方
                    IncludeLeader = false,
                    IncludeCharacters = true,
                    Filter = c => c.Id == selfId,
                },
                // 仅作用于此角色自身，且废弃区事件数达到该层阈值时该 +1000 生效
                Predicate = (st, sideIdx, card) =>
                {
                    if (card.Id != selfId) return false;
                    var owner = st.Players[ownerIdx];
                    int eventCount = owner.Trash.Count(c => c.Info.Kind == CardKind.Event);
                    return eventCount >= threshold;
                },
            });
        }

        return Task.CompletedTask;
    }
}
