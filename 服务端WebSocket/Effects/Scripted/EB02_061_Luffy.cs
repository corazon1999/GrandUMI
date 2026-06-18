using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-061 蒙奇·D·路飞（角色 / 暗 / 七水之城·草帽一伙，cost6 power7000，SEC）
/// 持续条件：我方领袖为多种颜色且对方场上存在 5 张或更多咚!! 的场合，此角色获得【速攻】。
/// 【攻击时】【每回合1次】可以将我方 2 张处于活跃状态的咚!! 放回咚!!卡组：
///   将此角色转为活跃状态。之后，将我方生命区最上方的 1 张卡牌加入手牌。
///
/// 实现说明：
///   - 条件【速攻】用 ContinuousEffect.GrantKeyword 注册（OnEnterField 时机），
///     Predicate 限定本卡自身、且我方领袖为多色（ColorList.Length>=2）、且对方费用区咚!!总数 >=5。
///   - 攻击段为每回合 1 次可选效果，成本为"将 2 张活跃咚!! 放回咚!!卡组"，
///     用 AtomicOps.ReturnDonToDeck(me, 2) 支付（需活跃咚 >=2）。
///     支付后将自身转为活跃（ActivateCard），再把生命区顶 1 张加入手牌。
/// </summary>
public class EB02_061_Luffy : IScriptedEffect
{
    public string CardNumber => "EB02-061";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int ownerIndex = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 条件【速攻】：我方领袖多色 且 对方咚!! >=5 时，自身获得【速攻】
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "速攻",
                Predicate = (s, sideIdx, card) =>
                    card.Id == selfId &&
                    sideIdx == ownerIndex &&
                    s.Players[ownerIndex].Leader.Info.ColorList.Length >= 2 &&
                    s.Players[1 - ownerIndex].TotalDonInCostArea >= 5,
            });
            return;
        }

        if (ctx.Trigger == EffectTrigger.OnAttackDeclare)
        {
            // 每回合 1 次
            var key = self.Info.Number + "-atk" + ":" + self.Id;
            if (me.TurnOnceUsed.Contains(key)) return;

            // 成本：需 2 张处于活跃状态的咚!!
            if (me.ActiveDonCount < 2) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "路飞【攻击时】：将 2 张活跃咚!! 放回咚!!卡组，使此角色转为活跃，并将生命区最上方 1 张加入手牌？");
            if (!use) return;

            me.TurnOnceUsed.Add(key);

            // 成本：将 2 张活跃咚!! 放回咚!!卡组
            AtomicOps.ReturnDonToDeck(me, 2);

            // 效果：将此角色转为活跃状态
            AtomicOps.ActivateCard(self);

            // 之后：将生命区最上方 1 张加入手牌
            if (me.LifeArea.Count > 0)
            {
                var lifeTop = me.LifeArea[0];
                me.LifeArea.RemoveAt(0);
                me.Hand.Add(lifeTop);
            }
            return;
        }
    }
}
