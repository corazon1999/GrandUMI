using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-055 妮古·罗宾（角色 / 光 / 草帽一伙，cost7 power8000）
/// 【登场时】可以将我方生命区最上方的 1 张卡牌放置到废弃区：我方领袖拥有《草帽一伙》特征的场合，
///   将我方卡组最上方的最多 2 张卡牌加入生命区最上方。
/// 【对方的回合中】【KO时】可以给予对方 1 点伤害。
///
/// 实现说明：
///   - 登场段为可选成本（"可以…：…"）：先 ConfirmOptional；无生命牌则无法支付。
///     支付（生命顶 1 张入废弃）后，仅当领袖含《草帽一伙》才卡组顶最多 2 张加生命（AddLifeFromDeckTop）。
///   - KO 段限定【对方的回合中】（CurrentTurnPlayer != OwnerIndex）；给对方 1 点伤害走
///     LifeRevealManager.DealDamageToLeader（会正常移对方生命并触发其生命触发牌）。无引擎（回放）则跳过。
/// </summary>
public class EB03_055_Robin : IScriptedEffect
{
    public string CardNumber => "EB03-055";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (me.LifeArea.Count == 0) return; // 无生命牌 → 无法支付成本

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "妮古·罗宾【登场时】：将生命区最上方 1 张放置废弃区，若领袖含《草帽一伙》则卡组顶最多 2 张加入生命？");
            if (!use) return;

            // 成本：生命区最上方 1 张放置废弃区
            var lifeTop = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            me.Trash.Add(lifeTop);

            // 生命牌离场 → 派发 OnLifeLeaveField，使"当生命卡牌离开场上时"类触发能响应
            // （如 OP11-041 奈美 我方回合中生命离场抽 1）。此前脚本直接移生命未派发事件，#156。
            await EffectRuntime.TriggerEvent(ctx.State, EffectTrigger.OnLifeLeaveField, ctx.Prompts,
                new Dictionary<string, object?> { ["owner"] = ctx.OwnerIndex, ["toZero"] = me.LifeArea.Count == 0 });

            // 收益：领袖含《草帽一伙》→ 卡组顶最多 2 张加入生命区最上方
            if (me.Leader.Info.HasKeyword("草帽一伙"))
                AtomicOps.AddLifeFromDeckTop(me, 2);
            return;
        }

        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return; // 仅【对方的回合中】
            if (ctx.Engine is not { } eng) return;                     // 回放等无引擎则跳过

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "妮古·罗宾【KO时】：给予对方 1 点伤害？");
            if (!use) return;

            await LifeRevealManager.DealDamageToLeader(eng, 1 - ctx.OwnerIndex, 1);
            return;
        }
    }
}
