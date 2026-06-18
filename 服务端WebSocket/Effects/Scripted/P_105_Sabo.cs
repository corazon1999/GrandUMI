using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-105 萨波（角色 / 地 / 革命军，cost4 power6000）
/// 我方领袖拥有《革命军》特征的场合，此角色获得【阻挡者】效果，费用+4。
/// 【登场时】可以将我方生命区最上方或最下方的 1 张卡牌加入手牌：
///   赋予我方 1 张领袖或角色最多 1 张休息状态的咚!!。
///
/// 实现说明：
///   - 持续条件赋予：领袖含《革命军》时自身获得【阻挡者】(GrantKeyword) + 费用+4(CostDelta)，
///     用 ContinuousEffect 注册（OnEnterField），Predicate 实时判领袖特征。
///   - 【登场时】可选成本：将生命顶/底 1 张加入手牌（ChooseOption 选顶或底）→ 赋予 1 张领袖/角色休息咚。
/// </summary>
public class P_105_Sabo : IScriptedEffect
{
    public string CardNumber => "P-105";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        // 持续条件：领袖含《革命军》→ 自身获得【阻挡者】+ 费用+4
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.Players[owner].Leader.Info.HasKeyword("革命军"),
        });
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            CostDelta = 4,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.Players[owner].Leader.Info.HasKeyword("革命军"),
        });

        // 【登场时】可选：将生命顶/底 1 张加入手牌 → 赋予 1 张领袖/角色休息咚
        if (me.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "萨波【登场时】：将生命区最上方或最下方 1 张卡牌加入手牌，赋予我方 1 张领袖/角色休息咚?");
        if (!use) return;

        int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将生命区哪张卡牌加入手牌？", new[] { "最上方", "最下方" });
        var lifeCard = where == 0 ? me.LifeArea[0] : me.LifeArea[me.LifeArea.Count - 1];
        me.LifeArea.Remove(lifeCard);
        me.Hand.Add(lifeCard);

        // 赋予我方 1 张领袖或角色最多 1 张休息状态的咚!!
        if (me.CostArea.Any(d => d.State == DonState.Active))
        {
            var targets = new List<CardInstance> { me.Leader };
            targets.AddRange(me.Characters);
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "选择 1 张领袖或角色，赋予 1 张休息状态的咚!!",
                targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pick.Count > 0)
            {
                var tgt = targets.First(c => c.Id.ToString() == pick[0]);
                AtomicOps.AttachDonFromCost(me, tgt.Id, 1, DonState.Rest);
            }
        }
    }
}
