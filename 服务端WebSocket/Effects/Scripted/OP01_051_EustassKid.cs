using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-051 尤斯塔斯·基德（角色 / 风 / 超新星·基德海盗团 / 8 费 8000）
/// 【咚!!×1】【对方的回合中】此角色处于休息状态的场合，对方不能攻击"尤斯塔斯·基德"以外的角色。
/// 【启动主要】【每回合1次】可以将此角色转为休息状态：将我方手牌中最多 1 张费用不高于 3 的角色卡牌登场。
///
/// 实现说明：
///   - 登场时注册条件性攻击目标锁定关键词：对方回合、本卡休息且附有咚×1时生效。
///   - 【启动主要】每回合1次，可选支付成本（横置自身），随后从手牌登场最多1张费用≤3角色。
/// </summary>
public class OP01_051_EustassKid : IScriptedEffect
{
    public string CardNumber => "OP01-051";

    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.OnEnterField or EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var selfId = self.Id;
            int owner = ctx.OwnerIndex;
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "仅可攻击角色：尤斯塔斯·基德",
                Predicate = (s, sideIdx, card) =>
                    sideIdx == owner && card.Id == selfId &&
                    s.CurrentTurnPlayer != owner && card.IsTapped &&
                    s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                    !card.IsEffectsNullified && !s.IsContinuouslyNullified(card),
            });
            return;
        }

        // 每回合1次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 候选：手牌中费用≤3 的角色
        var candidates = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.Cost <= 3).ToList();

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "基德【启动主要】：将此角色转为休息状态，登场手牌中最多 1 张费用≤3 的角色？");
        if (!use) return;

        // 成本：将此角色转为休息状态
        AtomicOps.RestCard(self);
        me.TurnOnceUsed.Add(key);

        if (candidates.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多 1 张费用≤3 的角色登场",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = candidates.First(c => c.Id.ToString() == chosen[0]);
            await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
