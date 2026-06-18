using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-097 贝加班克（领航 / 光 5000，科学家/艾格赫德）
/// 此领袖无法进行攻击。
/// 【启动主要】【每回合1次】①（可以横置费用区 1 张咚）：
///   将我方手牌中最多 1 张费用不高于 5 且拥有《艾格赫德》特征的卡牌正面朝上加入生命区最上方，或登场。
///
/// 实现说明 / 简化点：
///   - 「此领袖无法进行攻击」无永久限制时长，故在 OnGameStart 与每个 OnTurnStart 重新对领袖
///     施加 CannotAttack(UntilNextOpponentEndPhase)，从而每回合持续生效，等效永久无法攻击。
///   - 【启动主要】成本①横置 1 张活跃咚（手动转 Rest）；每回合 1 次。
///   - 「正面朝上加入生命区最上方」：me.Hand.Remove + me.LifeArea.Insert(0,..)（引擎无对应原子）。
///   - 「或登场」：用 PlayFromHandFree（角色/舞台）。事件卡不可登场，故仅角色/舞台提供「登场」选项。
/// </summary>
public class OP07_097_Vegapunk : IScriptedEffect
{
    public string CardNumber => "OP07-097";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnGameStart ||
        t == EffectTrigger.OnTurnStart ||
        t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 持续「无法攻击」：开局与每回合开始重新施加，等效永久
        if (ctx.Trigger == EffectTrigger.OnGameStart || ctx.Trigger == EffectTrigger.OnTurnStart)
        {
            if (!me.Leader.HasRestriction(RestrictionKind.CannotAttack))
                AtomicOps.AddRestriction(me.Leader, RestrictionKind.CannotAttack, KeywordDuration.UntilNextOpponentEndPhase);
            return;
        }

        // ── 【启动主要】【每回合1次】① ──
        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        var activeDon = me.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (activeDon.Count < 1) return;

        var candidates = me.Hand
            .Where(c => c.CurrentCost() <= 5 && c.Info.HasKeyword("艾格赫德"))
            .ToList();
        if (candidates.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "贝加班克【启动主要】：横置 1 张咚，将手牌中 1 张费用≤5 的《艾格赫德》卡牌加入生命区顶或登场？");
        if (!use) return;

        // 支付成本
        activeDon[0].State = DonState.Rest;
        me.TurnOnceUsed.Add(key);

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandEgghead",
            "选择最多 1 张费用≤5 的《艾格赫德》卡牌",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var card = candidates.First(c => c.Id.ToString() == chosen[0]);

        // 二选一：加入生命区最上方 / 登场（事件卡无法登场，只能加入生命区）
        bool canPlay = card.Info.Kind == CardKind.Character || card.Info.Kind == CardKind.Stage;
        int opt;
        if (canPlay)
            opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "处理该卡牌", new[] { "加入生命区最上方", "登场" });
        else
            opt = 0; // 事件卡只能加入生命区

        if (opt == 0)
        {
            me.Hand.Remove(card);
            me.LifeArea.Insert(0, card);
        }
        else
        {
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, card);
        }
    }
}
