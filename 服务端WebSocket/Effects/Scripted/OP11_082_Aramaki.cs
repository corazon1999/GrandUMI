using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-082 荒牧（角色）
/// 【启动主要】可以将此角色放置到废弃区：我方领袖拥有《海军》特征的场合，
///   本回合中，我方最多 1 张拥有《海军》特征的角色也可以攻击处于活跃状态的角色。
///   之后，将我方卡组最上方的 2 张卡牌放置到废弃区。
///
/// 实现说明：
/// - 成本为"将此角色放置到废弃区"，用 KO 自身（移入废弃区）实现。
/// - "可以攻击活跃角色"通过赋予【可攻击活跃】关键词（本回合）实现，ActionValidator 已支持。
/// - 仅当领袖拥有《海军》特征时才有收益，故仅在该前提下询问发动。
/// </summary>
public class OP11_082_Aramaki : IScriptedEffect
{
    public string CardNumber => "OP11-082";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 仅当领袖拥有《海军》特征时本效果有收益
        if (!me.Leader.Info.HasKeyword("海军")) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "荒牧【启动主要】：将此角色放置到废弃区，使我方最多 1 张《海军》角色本回合可攻击活跃角色，并将卡组顶 2 张放置到废弃区？");
        if (!use) return;

        // 成本：将此角色放置到废弃区
        AtomicOps.KO(ctx.State, ctx.OwnerIndex, self);

        // 效果 1：我方最多 1 张《海军》角色本回合可攻击活跃角色
        var navyChars = me.Characters.Where(c => c.Info.HasKeyword("海军")).ToList();
        if (navyChars.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                "选择最多 1 张《海军》角色，本回合可攻击活跃角色",
                navyChars.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = navyChars.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.GiveKeyword(tgt, "可攻击活跃", KeywordDuration.ThisTurn);
            }
        }

        // 效果 2：将我方卡组最上方的 2 张卡牌放置到废弃区
        AtomicOps.MillTop(me, 2);
    }
}
