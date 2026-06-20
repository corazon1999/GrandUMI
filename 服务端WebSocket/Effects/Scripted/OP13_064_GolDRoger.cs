using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-064 高路·D·罗杰（角色 10 费 13000，海盗王/罗杰海盗团）
/// 1. 持续：我方的领袖、以及所有"特征中不包含《罗杰海盗团》"的角色，效果无效。
///    （ContinuousEffect.NullifyEffect=true，Predicate 选中：源卡同方的领袖 + 不含《罗杰海盗团》的角色。
///     注意此角色自身含《罗杰海盗团》特征，故自身效果不受影响。）
/// 2. 【登场时】咚!!-3：直到下个对方的结束阶段结束时为止，我方领袖力量 +2000。
///    之后，直到下个对方的结束阶段结束时为止，对方所有角色力量 -2000。
///
/// 实现说明 / 简化点：
///   - "效果无效"持续状态用规范十三新增的 ContinuousEffect.NullifyEffect 通道实现。该通道仅看
///     Predicate（不读 Scope.Filter），故所有目标判定写入 Predicate。来源卡离场时引擎自动清理。
///   - "直到下个对方的结束阶段结束时为止"的力量修正用 ContinuousEffect.PowerDelta + Predicate
///     的有效期限制实现：注册前记下当前 TurnCount，Predicate 在 s.TurnCount <= 基准+2 时生效
///     （覆盖本回合 + 下个对方回合，近似"直到下个对方结束阶段结束"）。
///   - 咚!!-3 为发动成本：若活跃咚不足 3 则无法支付，不发动【登场时】收益（持续无效化第 1 条
///     已在 OnEnterField 注册，与登场时收益相互独立）。
/// </summary>
public class OP13_064_GolDRoger : IScriptedEffect
{
    public string CardNumber => "OP13-064";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var selfId = self.Id;

        // 防重复
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // ── 持续 1：我方领袖 + 不含《罗杰海盗团》特征的角色，效果无效 ──
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = true },
            NullifyEffect = true,
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                (card.Id == s.Players[owner].Leader.Id ||
                 (card.Info.Kind == CardKind.Character && !card.Info.HasKeyword("罗杰海盗团"))),
        });

        // ── 【登场时】咚!!-3 ──
        if (me.CostArea.Count < 3) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 3)) return;

        // 有效期基准：本回合 + 下个对方回合（近似"直到下个对方结束阶段结束时"）
        int baseTurn = ctx.State.TurnCount;

        // 持续 2：我方领袖力量 +2000（限期）
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                card.Id == s.Players[owner].Leader.Id &&
                s.TurnCount <= baseTurn + 2,
        });

        // 持续 3：对方所有角色力量 -2000（限期）
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = -2000,
            Predicate = (s, sideIdx, card) =>
                sideIdx == (1 - owner) &&
                card.Info.Kind == CardKind.Character &&
                s.TurnCount <= baseTurn + 2,
        });
    }
}
