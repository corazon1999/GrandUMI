using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-042 撒谎布（领航 4 费 5000，德莱斯罗兹/草帽一伙）
/// 1. 我方所有费用为 2 或更高且拥有《德莱斯罗兹》特征的角色费用 +1。（持续费用修正）
/// 2. 【对方的回合中】【每回合1次】当我方拥有《德莱斯罗兹》特征的角色被KO时，或因对方的效果
///    离开场上时，可以发动。我方手牌不多于 5 张的场合，抽取 1 张卡牌。
///
/// 实现说明：
///   - 能力 1 在领袖开局注册 ContinuousEffect.CostDelta = +1，离场/换领袖由引擎按 SourceCardId 清理。
///   - "费用为 2 或更高"按当前费用判定；求值时仅排除本效果自身的 +1，避免自我递归，
///     其他临时与持续费用修正仍会参与门槛判断。
///   - 能力 2 用 OnAnyCharKOd 覆盖战斗/效果 KO，用 OnCharLeaveField 覆盖非 KO 的效果离场；
///     后者通过 watcher payload 的 actingSide 严格限定为对方效果。
/// </summary>
public class OP10_042_Usopp : IScriptedEffect
{
    public string CardNumber => "OP10-042";

    public bool HandlesTrigger(EffectTrigger t) =>
        t is EffectTrigger.OnGameStart
            or EffectTrigger.OnAnyCharKOd
            or EffectTrigger.OnCharLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnGameStart)
        {
            RegisterCostIncrease(ctx);
            return;
        }

        await ResolveLeaveDraw(ctx);
    }

    private static void RegisterCostIncrease(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = c => c.Info.HasKeyword("德莱斯罗兹"),
            },
            CostDelta = 1,
            // Scope 仅供显示，作用对象必须写进 Predicate：仅我方场上、费用≥2 且《德莱斯罗兹》的角色，
            // 否则会漏到手牌/对方手牌/对方场上（#139）。
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner &&
                s.Players[owner].Characters.Contains(c) &&
                s.CurrentCostOfExcludingSource(sideIdx, c, selfId.ToString()) >= 2
                && c.Info.HasKeyword("德莱斯罗兹"),
        });
    }

    private static async Task ResolveLeaveDraw(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        var me = ctx.State.Players[owner];

        // 【对方的回合中】
        if (ctx.State.CurrentTurnPlayer == owner) return;

        // 【每回合1次】
        var key = $"OP10-042-leave:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key)) return;

        // 离场卡须为我方角色。
        int leaveOwner = ctx.Vars.TryGetValue("owner", out var ownerValue) && ownerValue is int value
            ? value
            : -1;
        if (leaveOwner != owner) return;

        if (ctx.Trigger == EffectTrigger.OnCharLeaveField)
        {
            // KO 统一交给 OnAnyCharKOd，避免同一次效果 KO 弹出两次可选发动确认。
            if (ctx.Vars.TryGetValue("isKo", out var koValue) && koValue is true) return;

            // 非 KO 离场仅限因对方的效果。
            int actingSide = ctx.Vars.TryGetValue("actingSide", out var actingValue) && actingValue is int side
                ? side
                : -1;
            if (actingSide != 1 - owner) return;
        }

        var cardId = ctx.Vars.TryGetValue("cardId", out var cardValue) ? cardValue as string : null;
        if (cardId is null) return;
        var left = FindCard(me, cardId);
        if (left is null || left.Info.Kind != CardKind.Character || !left.Info.HasKeyword("德莱斯罗兹")) return;

        // 手牌超过 5 张时即使发动也没有收益，不消耗每回合次数。
        if (me.Hand.Count > 5) return;

        bool use = await ctx.Prompts.ConfirmOptional(owner,
            "撒谎布【每回合1次】：我方《德莱斯罗兹》角色离场，抽取1张卡牌？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);
        AtomicOps.Draw(ctx.State, owner, 1);
    }

    private static CardInstance? FindCard(PlayerState player, string cardId)
        => player.Trash.FirstOrDefault(card => card.Id.ToString() == cardId)
           ?? player.Hand.FirstOrDefault(card => card.Id.ToString() == cardId)
           ?? player.Deck.FirstOrDefault(card => card.Id.ToString() == cardId)
           ?? player.LifeArea.FirstOrDefault(card => card.Id.ToString() == cardId)
           ?? player.Characters.FirstOrDefault(card => card.Id.ToString() == cardId);
}
