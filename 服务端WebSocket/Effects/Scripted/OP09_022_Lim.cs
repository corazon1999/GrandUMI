using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-022 莉姆（领航 / Leader）
/// 【启动主要】【每回合1次】可以将我方 3 张咚!! 转为休息状态：
///   从咚!! 卡组中追加最多 1 张休息状态的咚!!，
///   并从我方手牌中将最多 1 张费用不高于 5 且拥有《时光旅诗》特征的角色卡牌登场。
///
/// 实现说明 / 简化点：
///   - 卡面另含持续静态文本"我方的角色卡牌以休息状态登场"——属非力量持续修正（无引擎通道），
///     本脚本仅实现【启动主要】主动效果，静态登场休息部分未实现。
///   - 成本"将 3 张咚!!转为休息状态"通过直接把费用区 3 张活跃咚改为 Rest 实现；
///     活跃咚不足 3 张则无法支付，整段效果不发动。
///   - "费用不高于 5"取卡面 c.Info.Cost；《时光旅诗》为特征关键词，用 HasKeyword 判定。
/// </summary>
public class OP09_022_Lim : IScriptedEffect
{
    public string CardNumber => "OP09-022";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合 1 次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本检查：需要 3 张活跃咚
        int activeDon = me.CostArea.Count(d => d.State == DonState.Active);
        if (activeDon < 3) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "莉姆【启动主要】：将我方 3 张咚!!转为休息状态，追加 1 张休息咚并登场 1 张费用≤5 的《时光旅诗》角色？");
        if (!use) return;

        // 支付成本：3 张活跃咚 → 休息
        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= 3) break;
            if (d.State == DonState.Active) { d.State = DonState.Rest; rested++; }
        }

        me.TurnOnceUsed.Add(key);

        // 效果 1：从咚!!卡组追加最多 1 张休息状态的咚!!
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);

        // 效果 2：从手牌登场最多 1 张费用≤5 且拥有《时光旅诗》特征的角色
        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 5 &&
            c.Info.HasKeyword("时光旅诗")
        ).ToList();
        if (playable.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
                "登场最多 1 张费用≤5 的《时光旅诗》角色",
                playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = playable.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
            }
        }
    }
}
