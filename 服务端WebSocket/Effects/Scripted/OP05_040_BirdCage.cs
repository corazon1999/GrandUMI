using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-040 鸟笼（舞台）
/// 持续：我方领袖为"堂吉诃德·多弗拉门戈"的场合，所有费用不高于 5 的角色在双方的重置阶段中
///       不会转为活跃状态。（用 ContinuousEffect.PreventReset 注册，Side=-1 双方。）
/// 【我方的回合结束时】我方场上有 10 张咚!! 的场合，将所有处于休息状态且费用不高于 5 的角色 KO。
///       之后，将此舞台放置到废弃区。
///
/// 说明：
/// - PreventReset 谓词在重置阶段被引擎查询；sideIdx 为该卡所属方，费用用 CurrentCostOf 取当前值。
/// - 领袖名条件随时可能变化（领袖一般固定），谓词内实时判断。
/// - 回合结束时 KO 我方与对方双方所有休息≤5费角色（文本为"所有"）。
/// </summary>
public class OP05_040_BirdCage : IScriptedEffect
{
    public string CardNumber => "OP05-040";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnMyTurnEnd;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 注册持续：领袖为多弗拉门戈时，双方所有费用≤5角色在重置阶段不转为活跃。
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = -1, IncludeLeader = false, IncludeCharacters = true },
                PreventReset = true,
                Predicate = (s, sideIdx, card) =>
                    s.Players[owner].Leader.Info.NameContains("多弗拉门戈") &&
                    s.CurrentCostOf(sideIdx, card) <= 5,
            });
            return Task.CompletedTask;
        }

        // OnMyTurnEnd：场上 10 张咚!! 时，KO 双方所有休息且费用≤5的角色，然后此舞台进废弃区。
        if (me.TotalDonInCostArea < 10) return Task.CompletedTask;

        for (int side = 0; side < 2; side++)
        {
            var p = ctx.State.Players[side];
            var victims = p.Characters
                .Where(c => c.IsTapped && ctx.State.CurrentCostOf(side, c) <= 5)
                .ToList();
            foreach (var v in victims)
                AtomicOps.KO(ctx.State, side, v);
        }

        // 之后：将此舞台放置到废弃区
        if (me.StageCard == self)
        {
            me.StageCard = null;
            me.Trash.Add(self);
        }
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        return Task.CompletedTask;
    }
}
