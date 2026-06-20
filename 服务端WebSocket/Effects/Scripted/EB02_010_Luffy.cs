using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-010 蒙奇·D·路飞（领航 / 风·暗 / 草帽一伙）
/// 【启动主要】【每回合1次】咚!!-2：我方场上的角色仅为拥有《草帽一伙》特征的角色的场合，
///   将我方最多 2 张咚!! 转为活跃状态。之后，直到下个对方的回合结束时为止，此领袖的力量 +1000。
///
/// 实现说明：
///   - 成本「咚!!-2」用 ReturnDonToDeck(me, 2)，发动前先确认费用区有≥2张咚可放回。
///   - 条件「我方场上角色仅为草帽一伙」= 所有角色都含《草帽一伙》(无角色时空真，亦满足)。
///   - 「将最多 2 张咚转为活跃」遍历费用区休息咚置为活跃；放回 2 张后再活跃，净效果可能不足 2 张，
///     按规则只活跃当前可活跃的休息咚（最多 2）。
///   - 领袖 +1000「直到下个对方回合结束」用 ContinuousEffect + TurnCount 时效（baseTurn..+2）。
///   - 每回合1次用 TurnOnceUsed。
/// </summary>
public class EB02_010_Luffy : IScriptedEffect
{
    public string CardNumber => "EB02-010";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;

        // 条件：我方场上角色仅为《草帽一伙》
        if (!me.Characters.All(c => c.Info.HasKeyword("草帽一伙"))) return;

        // 成本：咚!!-2，需有≥2张可放回的咚
        if (me.CostArea.Count < 2) return;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞领航【启动主要】：咚!!-2，将最多 2 张咚!! 转为活跃，并使此领袖力量 +1000（直到下个对方回合结束）？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);

        // 成本：咚!!-2
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 2)) return;

        // 将最多 2 张休息咚转为活跃
        int n = 0;
        foreach (var d in me.CostArea)
        {
            if (n >= 2) break;
            if (d.State == DonState.Rest) { d.State = DonState.Active; n++; }
        }

        // 之后：此领袖力量 +1000，直到下个对方回合结束
        int baseTurn = ctx.State.TurnCount;
        var selfId = self.Id;
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner && card.Id == selfId && s.TurnCount < baseTurn + 2,
        });
    }
}
