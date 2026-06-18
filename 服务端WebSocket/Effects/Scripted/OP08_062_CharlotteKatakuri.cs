using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-062 夏洛特·卡塔库栗（角色）
/// 【启动主要】可以将此角色放置到废弃区：我方领袖拥有《大妈海盗团》特征的场合，
///   将我方手牌中最多 1 张费用为 3 或更高、且费用不高于对方场上咚!!张数的"夏洛特·卡塔库栗"登场。
///
/// 实现说明：
///   - 成本"将此角色放置到废弃区"用 KO(ctx.State, ctx.OwnerIndex, self) 实现（自身离场进废弃区）。
///   - 登场费用上限是动态值"对方场上咚!!的张数"，在脚本中按当前局面实时计算。
///   - 仅当领袖含《大妈海盗团》特征时有收益，故先判定该前提，不满足直接不发动。
/// </summary>
public class OP08_062_CharlotteKatakuri : IScriptedEffect
{
    public string CardNumber => "OP08-062";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 前提：我方领袖拥有《大妈海盗团》特征
        if (!me.Leader.Info.HasKeyword("大妈海盗团")) return;

        // 动态费用上限 = 对方场上咚!!的张数
        int donCount = opp.TotalDonInCostArea;

        // 手牌中可登场的"夏洛特·卡塔库栗"：费用 3 ≤ cost ≤ donCount
        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Name.Contains("夏洛特·卡塔库栗") &&
            c.Info.Cost >= 3 &&
            c.Info.Cost <= donCount
        ).ToList();

        // 若没有可登场目标，发动成本无意义，不发动
        if (playable.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "卡塔库栗【启动主要】：将此角色放置到废弃区，登场 1 张手牌中费用3~对方咚数的『夏洛特·卡塔库栗』？");
        if (!use) return;

        // 成本：将此角色放置到废弃区
        AtomicOps.KO(ctx.State, ctx.OwnerIndex, self);

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场最多 1 张手牌中的『夏洛特·卡塔库栗』",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
