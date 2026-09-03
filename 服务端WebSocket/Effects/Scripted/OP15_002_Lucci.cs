using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-002 路西（领航）
/// 【攻击时】/【对方的攻击时】：可丢弃任意张事件/舞台，每张本次战斗本领袖力量 +1000（反馈#208）
/// 启动主要每回合 1 次：若本回合已经发动过原始费用 ≥3 的事件，抽 1
/// </summary>
public class OP15_002_Lucci : IScriptedEffect
{
    public string CardNumber => "OP15-002";
    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.ActivatedMain
        || t == EffectTrigger.OnAttackDeclare
        || t == EffectTrigger.OnOppAttackDeclare;
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 反馈#208：【攻击时】/【对方的攻击时】可丢弃任意张事件/舞台，每丢 1 张本次战斗本领袖力量 +1000。
        if (ctx.Trigger == EffectTrigger.OnAttackDeclare || ctx.Trigger == EffectTrigger.OnOppAttackDeclare)
        {
            var candidates = me.Hand
                .Where(c => c.Info.Kind == CardKind.Event || c.Info.Kind == CardKind.Stage)
                .ToList();
            if (candidates.Count == 0) return;
            // 手牌为私有区，须显式下发 choiceCards 番号，客户端才能显示卡图。
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OP15-002-DiscardForPower",
                "路西：可丢弃任意张事件/舞台，每丢 1 张本次战斗本领袖力量 +1000（可放弃）",
                candidates.Select(c => c.Id.ToString()).ToList(), 0, candidates.Count, extra);
            foreach (var cid in chosen)
            {
                var card = candidates.FirstOrDefault(c => c.Id.ToString() == cid);
                if (card is null) continue;
                AtomicOps.DiscardHand(me, card);
                AtomicOps.AddPowerThisBattle(me.Leader, 1000); // 每张 +1000，仅本次战斗
            }
            return;
        }

        // 【启动主要】【每回合 1 次】：本回合已经发动过原始费用≥3 的事件时，抽 1。
        // 条件未满足时不发动、不消耗每回合次数；事件历史由 EffectRuntime 在所有发动入口统一记录。
        const string key = "OP15-002-MainOncePerTurn";
        if (me.TurnOnceUsed.Contains(key)) return;
        if (!me.HasActivatedBaseCost3PlusEventThisTurn) return;
        me.TurnOnceUsed.Add(key);
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
    }
}
