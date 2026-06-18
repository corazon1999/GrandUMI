using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-041 波尔·汉库珂（领航）
/// 第一段【对方的回合中】当我方角色登场时，抽取 1 张卡牌。
/// 第二段【咚!!×1】【每回合1次】当我方原本力量为 5000 或更高且拥有《亚马逊·百合》或
///   《九蛇海盗团》特征的角色被 KO 时，将对方生命区最上方的最多 1 张卡牌加入其持有者手牌。
///
/// 第一段：OnAllyCharEnter watcher（payload owner=登场方、cardId=登场卡），仅对方回合、登场方为我方、非自身。
/// 第二段：OnAnyCharKOd watcher（payload owner=被KO方、cardId）。被KO卡此刻在我方废弃区，读其 Info 判定
///   原本力量≥5000 且特征含《亚马逊·百合》或《九蛇海盗团》；【咚!!×1】= 自身被赋予咚≥1；满足则我方可选
///   将对方生命顶最多 1 张加入对方手牌（持有者=对方）。【每回合1次】。
/// （06-17 诊断"OnCharLeaveField 无法读被KO卡 Info"已过时：引擎已有 OnAnyCharKOd watcher，
///   被KO卡仍在废弃区、Info 为静态印刷数据可直接读，故第二段现可实现。）
/// </summary>
public class OP14_041_BoaHancock : IScriptedEffect
{
    public string CardNumber => "OP14-041";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnAllyCharEnter ||
        t == EffectTrigger.OnAnyCharKOd;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnAllyCharEnter) { ResolveAbility1(ctx); return; }
        if (ctx.Trigger == EffectTrigger.OnAnyCharKOd) { await ResolveAbility2(ctx); return; }
    }

    // 第一段【对方的回合中】我方角色登场时抽 1
    private void ResolveAbility1(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;

        // 仅【对方的回合中】
        if (ctx.State.CurrentTurnPlayer == owner) return;

        // payload：登场方须为我方
        int enterOwner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (enterOwner != owner) return;

        // 登场卡非自身（领袖不会经此触发，仍稳妥判一次）
        var cardId = ctx.Vars.TryGetValue("cardId", out var cv) ? cv as string : null;
        if (cardId is not null && cardId == ctx.Source.Id.ToString()) return;

        AtomicOps.Draw(ctx.State, owner, 1);
    }

    // 第二段【咚!!×1】【每回合1次】我方≥5000 且亚马逊·百合/九蛇海盗团角色被KO → 对方生命顶≤1 入对方手牌
    private async Task ResolveAbility2(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 【咚!!×1】：此领袖被赋予咚 ≥ 1
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return;

        // 【每回合1次】
        var key = "OP14-041-Ability2:" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 被KO者须为我方角色
        int koOwner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (koOwner != ctx.OwnerIndex) return;

        // 读被KO卡（此刻在我方废弃区）的原本力量与特征
        var cardId = ctx.Vars.TryGetValue("cardId", out var cv) ? cv as string : null;
        var koCard = cardId is not null ? me.Trash.FirstOrDefault(c => c.Id.ToString() == cardId) : null;
        if (koCard is null) return;
        if (koCard.Info.Power < 5000) return;
        if (!(koCard.Info.HasKeyword("亚马逊·百合") || koCard.Info.HasKeyword("九蛇海盗团"))) return;

        // 对方生命顶最多 1 张入对方手牌（我方可选发动）
        if (opp.LifeArea.Count == 0) return;
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "汉库克：将对方生命区最上方 1 张卡牌加入对方手牌？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);
        var top = opp.LifeArea[0];
        opp.LifeArea.RemoveAt(0);
        top.IsLifeFaceUp = false;
        opp.Hand.Add(top);
    }
}
