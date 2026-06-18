using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-002 波特夹斯·D·艾斯（领航 3 费 6000，白胡子海盗团）
/// 能力1【对方的攻击时】【每回合1次】可以丢弃我方的 1 张手牌：
///   本次战斗中，对方最多 1 张领袖或角色力量 -2000。
/// 能力2【咚!!×1】【每回合1次】当我方受到伤害时或我方原本的力量为 6000 或更高的角色被 KO 时，抽取 1 张卡牌。
///
/// 能力1：监听 OnOppAttackDeclare。
/// 能力2：监听 OnDamageToLeader（payload defenderOwner=受伤方，取我方领袖受伤）
///        + OnAnyCharKOd（payload owner=被KO方 / cardId，取我方原本力量≥6000 角色被KO）。
///        【咚!!×1】= 此领袖被赋予咚≥1；【每回合1次】两路触发共享一个 once key（一回合只抽 1 次）。
/// （引擎已派发 OnDamageToLeader 与 OnAnyCharKOd，effectTags 已补两标签 → 可被 CollectListeners 收集。）
/// </summary>
public class OP13_002_Ace : IScriptedEffect
{
    public string CardNumber => "OP13-002";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnOppAttackDeclare ||
        t == EffectTrigger.OnDamageToLeader ||
        t == EffectTrigger.OnAnyCharKOd;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnOppAttackDeclare)
        {
            await ResolveAbility1(ctx);
            return;
        }
        await ResolveAbility2(ctx);
    }

    // 能力1：对方攻击时，弃 1 手牌使对方最多 1 张领袖/角色本次战斗 -2000
    private async Task ResolveAbility1(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var key = "OP13-002-OppAttackOncePerTurn" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 代价需要至少 1 张手牌
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "艾斯：弃 1 张手牌，使对方最多 1 张领袖或角色本次战斗力量 -2000？");
        if (!use) return;

        // 支付代价：弃 1 张手牌（玩家自选），客户端经 choiceCards 显示卡面
        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var dchosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardOwnChosen",
            "丢弃 1 张手牌作为代价",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, discardExtra);
        var toDiscard = dchosen.Count > 0
            ? me.Hand.FirstOrDefault(c => c.Id.ToString() == dchosen[0])
            : me.Hand.FirstOrDefault();
        if (toDiscard is null) return;
        AtomicOps.DiscardHand(me, toDiscard);

        me.TurnOnceUsed.Add(key);

        // 效果：对方领袖或角色最多 1 张本次战斗力量 -2000
        var targets = new List<CardInstance> { opp.Leader };
        targets.AddRange(opp.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeaderOrCharacter",
            "选最多 1 张对方领袖或角色，本次战斗力量 -2000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = targets.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is not null)
            AtomicOps.AddPowerThisBattle(target, -2000);
    }

    // 能力2【咚!!×1】【每回合1次】我方受到伤害时 / 我方原本力量≥6000 角色被KO时，抽 1
    private Task ResolveAbility2(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【咚!!×1】：此领袖被赋予咚 ≥ 1
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return Task.CompletedTask;

        // 【每回合1次】：能力2 两路触发（受伤 / KO）共享
        var key = "OP13-002-Ability2" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;

        bool fire = false;
        if (ctx.Trigger == EffectTrigger.OnDamageToLeader)
        {
            // 我方领袖受到伤害（payload defenderOwner=受伤方）
            int defenderOwner = ctx.Vars.TryGetValue("defenderOwner", out var dv) && dv is int di ? di : -1;
            fire = defenderOwner == ctx.OwnerIndex;
        }
        else if (ctx.Trigger == EffectTrigger.OnAnyCharKOd)
        {
            // 我方原本力量≥6000 的角色被KO（被KO卡此刻在我方废弃区，Info 为静态印刷数据）
            int koOwner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
            if (koOwner == ctx.OwnerIndex)
            {
                var cardId = ctx.Vars.TryGetValue("cardId", out var cv) ? cv as string : null;
                var koCard = cardId is not null ? me.Trash.FirstOrDefault(c => c.Id.ToString() == cardId) : null;
                if (koCard is not null && koCard.Info.Power >= 6000) fire = true;
            }
        }
        if (!fire) return Task.CompletedTask;

        me.TurnOnceUsed.Add(key);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        return Task.CompletedTask;
    }
}
