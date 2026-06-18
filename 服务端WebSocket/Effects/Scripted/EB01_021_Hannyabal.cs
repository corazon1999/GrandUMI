using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-021 汉尼拔（领航 / 水·暗 / 因佩尔地狱）
/// 【我方的回合结束时】可以将我方1张费用为2或更高且拥有《因佩尔地狱》特征的角色放回其持有者的手牌：
///   从咚!!卡组中追加最多1张活跃状态的咚!!。
///
/// 实现说明：
///   - 成本：将我方1张费用≥2且含《因佩尔地狱》的角色放回手牌。
///   - 收益：从咚!!卡组追加最多1张活跃咚（RefreshDonFromDeck）。
/// </summary>
public class EB01_021_Hannyabal : IScriptedEffect
{
    public string CardNumber => "EB01-021";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var cands = me.Characters.Where(c =>
            c.Info.Cost >= 2 && c.Info.HasKeyword("因佩尔地狱")).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "汉尼拔【我方的回合结束时】：将1张费用≥2且含《因佩尔地狱》的角色放回手牌，从咚!!卡组追加1张活跃咚？");
        if (!use) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方1张费用≥2且含《因佩尔地狱》的角色放回手牌",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count == 0) return;

        // 成本：放回手牌
        var tgt = cands.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, tgt);

        // 收益：从咚!!卡组追加最多1张活跃咚
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
    }
}
