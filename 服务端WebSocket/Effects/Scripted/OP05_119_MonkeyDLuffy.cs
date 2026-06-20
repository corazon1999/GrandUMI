using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-119 蒙奇·D·路飞（角色）
/// 【登场时】咚!!-10：将此角色以外的我方所有角色自选顺序放回卡组最下方。
///   之后，在本回合之后追加获得我方的回合。
/// 【启动主要】【每回合1次】①：从咚!!卡组中追加最多 1 张活跃状态的咚!!。
///
/// 实现说明 / 简化点：
///  - 【登场时】要求支付咚!!-10（将 10 张咚放回咚卡组）。这是发动成本，仅当合计咚≥10 时可发动。
///    用 ConfirmOptional 询问是否发动（"咚!!-10"成本较大，按可选处理）。
///  - "自选顺序放回卡组最下方"实现为逐张放底（保持移除顺序），对实战影响极小。
///  - 追加回合用 ctx.State.ExtraTurnPending = true（本回合结束后同一玩家再来一回合）。
///  - 【启动主要】①成本（横置 1 张活跃咚）按惯例由引擎/打出流程扣除，这里只实现收益：
///    从咚卡组追加最多 1 张活跃咚（RefreshDonFromDeck）。每回合 1 次用 TurnOnceUsed 控制。
/// </summary>
public class OP05_119_MonkeyDLuffy : IScriptedEffect
{
    public string CardNumber => "OP05-119";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 成本：咚!!-10（合计咚不足 10 张则无法发动）
            if (me.TotalDonInCostArea < 10) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "路飞【登场时】：咚!!-10，将此角色以外的我方所有角色放回卡组最下方，之后追加获得我方的回合？");
            if (!use) return;

            // 支付成本：咚!!-10
            if (!await AtomicOps.PromptReturnDonToDeck(ctx, 10)) return;

            // 将此角色以外的我方所有角色放回卡组最下方
            var others = me.Characters.Where(c => c.Id != self.Id).ToList();
            foreach (var c in others)
                AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, c);

            // 之后：在本回合之后追加获得我方的回合
            ctx.State.ExtraTurnPending = true;
            return;
        }

        // ── 【启动主要】【每回合1次】①：从咚!!卡组追加最多 1 张活跃咚!! ──
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);

        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
    }
}
