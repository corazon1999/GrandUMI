using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-068 道格拉斯·巴雷特（暗 3 费 4000，FILM/原罗杰海盗团）
/// 持续：我方场上存在 8 张或更多咚!! 的场合，此角色的力量 +2000。
/// 【登场时】我方领袖拥有的特征中包含〈罗杰海盗团〉的场合，
///   从咚!!卡组中追加最多 1 张休息状态的咚!!。
///
/// 实现说明：
///   - 持续力量通过 ContinuousEffect 注册：Predicate 判定我方费用区咚!!总数 ≥8 时，
///     对自身 +2000；来源卡离场时由引擎清理。重复登场前先按 SourceCardId 去重避免叠加。
///   - "8 张或更多咚!!"按费用区内全部咚!!计（含活跃/休息/被赋予中），即 TotalDonInCostArea。
///   - 【登场时】仅在我方领袖含〈罗杰海盗团〉特征时，从咚!!卡组追加 1 张休息状态咚（强制，尽力）。
/// </summary>
public class OP13_068_Barrett : IScriptedEffect
{
    public string CardNumber => "OP13-068";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int ownerIndex = ctx.OwnerIndex;
        var selfId = self.Id;

        // ── 注册持续：我方场上 ≥8 张咚!! 时，此角色 +2000 ──
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[ownerIndex].TotalDonInCostArea >= 8,
        });

        // ── 【登场时】领袖含〈罗杰海盗团〉→ 从咚!!卡组追加最多 1 张休息状态咚 ──
        if (me.Leader.Info.HasKeyword("罗杰海盗团"))
        {
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
        }

        return Task.CompletedTask;
    }
}
