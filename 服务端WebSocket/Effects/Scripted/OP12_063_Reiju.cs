using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-063 温思默克·丽久（暗 / 角色）
/// 效果原文：
///   我方废弃区中有 4 张或更多事件的场合，此角色的力量 +2000，费用 +5。
///   【阻挡者】（对方攻击后，可以将此卡牌转为休息状态，并将攻击对象改为此卡牌）
///
/// 实现说明 / 简化点：
///   - 条件式"力量 +2000"为永续静态加成：在【登场时】向 GameState.ContinuousEffects
///     注册一个 +2000 的永续效果，Predicate 实时统计我方废弃区中的事件数量，
///     废弃区事件 ≥4 时生效，随废弃区变化自动增减；Filter 限定仅作用于此角色自身。
///     来源卡离场后由引擎自动清理该永续效果。注册具幂等性（避免重复登场叠加）。
///   - 条件式"费用 +5"未实现：引擎的 ContinuousEffect 仅支持力量修正，
///     无条件式静态费用修正能力（与 OP12-042 同类缺口）。
///   - 【阻挡者】为卡面基础关键字，由卡牌数据处理，无需脚本。
/// </summary>
public class OP12_063_Reiju : IScriptedEffect
{
    public string CardNumber => "OP12-063";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var state = ctx.State;
        var selfId = ctx.Source.Id;
        int ownerIdx = ctx.OwnerIndex;

        // 幂等：先移除本卡已注册过的同源永续效果，避免重复登场叠加
        state.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        state.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            PowerDelta = 2000,
            Scope = new ContinuousScope
            {
                Side = 0,                 // 仅我方
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = c => c.Id == selfId,
            },
            // 仅作用于此角色自身，且我方废弃区事件数 ≥4 时该 +2000 生效
            Predicate = (st, sideIdx, card) =>
            {
                if (card.Id != selfId) return false;
                var owner = st.Players[ownerIdx];
                int eventCount = owner.Trash.Count(c => c.Info.Kind == CardKind.Event);
                return eventCount >= 4;
            },
        });

        return Task.CompletedTask;
    }
}
