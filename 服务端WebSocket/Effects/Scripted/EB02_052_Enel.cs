using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-052 艾尼路（角色 / 光 / 空岛，cost10 power11000）
/// 我方领袖拥有《空岛》特征的场合，此角色获得【速攻】效果。（条件持续 → GrantKeyword）
/// 【攻击时】可以丢弃我方的 1 张手牌：我方生命卡牌不多于 1 张的场合，
///   将我方卡组最上方的最多 1 张卡牌加入生命区最上方。之后，本回合中，此角色的力量 +1000。
///
/// 实现说明：
///   - 条件【速攻】用 ContinuousEffect.GrantKeyword（OnEnterField 注册）：仅自身、且领袖含《空岛》。
///   - 【攻击时】可选成本「弃 1 张手牌」：先 ConfirmOptional，无手牌则不可发动。
///     支付后，生命≤1 则卡组顶 1 张入生命，然后自身本回合 +1000（+1000 无条件，文本"之后"段）。
/// </summary>
public class EB02_052_Enel : IScriptedEffect
{
    public string CardNumber => "EB02-052";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var selfId = self.Id;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 条件持续【速攻】：领袖含《空岛》时，自身获得【速攻】
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "速攻",
                Predicate = (s, sideIdx, card) =>
                    sideIdx == owner && card.Id == selfId &&
                    s.Players[owner].Leader.Info.HasKeyword("空岛"),
            });
            return;
        }

        // OnAttackDeclare：可选弃 1 手牌
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "艾尼路【攻击时】：丢弃 1 张手牌？（生命≤1 则卡组顶 1 张入生命，之后此角色本回合 +1000）");
        if (!use) return;

        // 成本：丢弃我方 1 张手牌
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count < 1) return;
        var discard = me.Hand.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.DiscardHand(me, discard);

        // 生命≤1 → 卡组顶 1 张入生命区最上方
        if (me.LifeArea.Count <= 1)
            AtomicOps.AddLifeFromDeckTop(me, 1);

        // 之后：本回合中，此角色力量 +1000
        AtomicOps.AddPowerThisTurn(self, 1000);
    }
}
