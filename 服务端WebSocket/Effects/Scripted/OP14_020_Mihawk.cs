using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-020 杰拉基尔·米霍克（领航）
/// 持续：对方领袖拥有属性（斩）的场合，此领袖的力量 +1000。
///   （ContinuousEffect.PowerDelta，OnGameStart 注册，Predicate 判对方领袖 Property 含"斩"。）
/// 【启动主要】【每回合1次】可以将我方的 1 张卡牌转为休息状态：
///   场上存在费用为 5 或更高的角色的场合，将我方最多 3 张咚!! 转为活跃状态。
///   之后，本回合中，我方无法登场角色卡牌。
///
/// 实现说明 / 简化点：
///   - 成本"将我方 1 张卡牌转为休息状态"= 活跃的 领袖/角色/舞台/咚!! 选 1 张横置
///     （走 AtomicOps.PromptRestOwnCards 混选）。若无可横置目标则不发动。
///   - "场上存在费用为 5 或更高的角色"按双方角色当前费用判定（CurrentCostOf）。
///   - "将我方最多 3 张咚!!转为活跃状态"= 把费用区中最多 3 张休息咚改为活跃。
///   - 之后用 NoPlayCharacterThisTurn 标记本回合我方无法登场角色（回合结束自动清除）。
/// </summary>
public class OP14_020_Mihawk : IScriptedEffect
{
    public string CardNumber => "OP14-020";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnGameStart || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnGameStart)
        {
            RegisterContinuous(ctx);
            return;
        }

        // ── 【启动主要】【每回合1次】──
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var key = CardNumber + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // "可以" → 询问是否发动
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "米霍克【启动主要】：将我方 1 张卡牌转为休息状态，将最多 3 张咚!! 转为活跃（之后本回合无法登场角色）？");
        if (!use) return;

        // 成本：将我方 1 张活跃 领袖/角色/舞台/咚 转为休息状态
        if (!await AtomicOps.PromptRestOwnCards(ctx, 1,
            "选择我方 1 张卡牌转为休息状态（成本，可选活跃 领袖/角色/舞台/咚!!）")) return;

        me.TurnOnceUsed.Add(key);

        // 条件：场上存在费用为 5 或更高的角色（双方）
        bool hasCost5 =
            me.Characters.Any(c => ctx.State.CurrentCostOf(ctx.OwnerIndex, c) >= 5) ||
            opp.Characters.Any(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) >= 5);

        if (hasCost5)
        {
            // 将我方最多 3 张休息咚!! 转为活跃状态
            int activated = 0;
            foreach (var d in me.CostArea)
            {
                if (activated >= 3) break;
                if (d.State == DonState.Rest)
                {
                    d.State = DonState.Active;
                    activated++;
                }
            }
        }

        // 之后，本回合中我方无法登场角色卡牌
        ctx.State.NoPlayCharacterThisTurn.Add(ctx.OwnerIndex);
    }

    private static void RegisterContinuous(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            PowerDelta = 1000,
            // 仅此领袖自身；且对方领袖属性含"斩"时生效
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[1 - owner].Leader.Info.Property.Contains("斩"),
        });
    }
}
