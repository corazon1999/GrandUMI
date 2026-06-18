using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-108 杰丽·邦妮（角色 / 光 9 费 10000，艾格赫德/邦妮海盗团）
///
/// 完整文本：
///   【触发】我方生命卡牌不多于 1 张的场合，将对方最多 1 张费用不高于 7 的角色转为休息状态。
///   【登场时】我方领袖拥有《艾格赫德》特征的场合，本回合中，此角色获得【速攻】效果。
///     之后，对方将其生命区最上方的 1 张卡牌加入手牌。
///
/// 实现：
///   - 生命牌【触发】(OnLifeRevealTrigger)：生命 ≤1 时，从对方费用 ≤7 的角色中选最多 1 张转休息（可选 min=0）。
///   - 【登场时】(OnEnterField)：
///       · 领袖含《艾格赫德》→ 本回合本卡获得【速攻】（强制）。
///       · 之后，对方把其生命区最上方 1 张卡牌加入手牌（强制；对方无生命牌则跳过）。
///         此处直接执行区域移动（生命顶 → 对方手牌），不弹对方选择窗口（仅 1 张，无可选项）。
///
/// 简化点：
///   - “对方将其生命区最上方 1 张卡牌加入手牌”不区分该生命牌朝向/触发（直接入手），与现有生命区建模一致。
/// </summary>
public class OP13_108_JewelryBonney : IScriptedEffect
{
    public string CardNumber => "OP13-108";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnLifeRevealTrigger || t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 生命卡牌不多于 1 张：将对方最多 1 张费用 ≤7 的角色转休息
            if (me.LifeCount > 1) return;
            var candidates = opp.Characters.Where(c => c.Info.Cost <= 7).ToList();
            if (candidates.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张费用 ≤7 的角色转为休息状态",
                candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count == 0) return;
            var tgt = candidates.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
            return;
        }

        // OnEnterField
        // 1. 领袖拥有《艾格赫德》→ 本回合本卡获得【速攻】
        if (me.Leader.Info.HasKeyword("艾格赫德"))
        {
            AtomicOps.GiveKeyword(ctx.Source, "速攻", KeywordDuration.ThisTurn);
        }

        // 2. 之后，对方将其生命区最上方 1 张卡牌加入手牌（强制）
        if (opp.LifeArea.Count > 0)
        {
            var top = opp.LifeArea[0];
            opp.LifeArea.RemoveAt(0);
            opp.Hand.Add(top);
        }
    }
}
