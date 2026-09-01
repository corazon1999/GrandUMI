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
/// 实现说明：
///   - "效果无效"持续状态用规范十三新增的 ContinuousEffect.NullifyEffect 通道实现。该通道仅看
///     Predicate（不读 Scope.Filter），故所有目标判定写入 Predicate。来源卡离场时引擎自动清理。
///   - "对方所有角色"在效果结算时确定目标快照；之后登场的角色不受影响。力量修正使用
///     PowerModsUntilOppEnd，并由回合引擎在效果施加方的下个对方结束阶段精确清除。
///   - 咚!!-3 为发动成本：若活跃咚不足 3 则无法支付，不发动【登场时】收益（持续无效化第 1 条
///     通过 IFieldStaticEffect 独立注册，与登场时收益相互独立）。
/// </summary>
public class OP13_064_GolDRoger : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "OP13-064";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task RegisterFieldStatic(EffectContext ctx)
    {
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

        return Task.CompletedTask;
    }

    public async Task Resolve(EffectContext ctx)
    {
        await RegisterFieldStatic(ctx);

        var me = ctx.State.Players[ctx.OwnerIndex];

        // ── 【登场时】咚!!-3 ──
        if (me.CostArea.Count < 3) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 3)) return;

        // 已结算的跨回合修正只作用于此刻在场的角色；后续登场角色不属于“对方所有角色”的结算快照。
        AtomicOps.AddPowerUntilOppEnd(me.Leader, 2000, ctx.OwnerIndex);
        foreach (var character in ctx.State.Players[1 - ctx.OwnerIndex].Characters.ToList())
            AtomicOps.AddPowerUntilOppEnd(character, -2000, ctx.OwnerIndex);
    }
}
