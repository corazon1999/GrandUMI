using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-112 大和（角色 / 光 / 和之国）
/// 【登场时】将对方最多 1 张费用不高于"双方生命卡牌合计张数"的角色 KO。
///   之后，我方生命卡牌不多于 1 张的场合，将我方卡组最上方的最多 1 张卡牌加入生命区最上方。
///
/// 实现说明：
///   - 动态费用阈值 = 我方 LifeCount + 对方 LifeCount，C# 运行时计算后过滤候选。
///   - 后半段：登场时立即结算"我方生命≤1则卡组顶 1 张入生命顶"。
/// </summary>
public class OP04_112_Yamato : IScriptedEffect
{
    public string CardNumber => "OP04-112";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        int threshold = me.LifeCount + opp.LifeCount;

        var cands = opp.Characters
            .Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= threshold)
            .ToList();
        if (cands.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                $"将对方最多 1 张费用≤{threshold} 的角色 KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }

        // 之后：我方生命≤1 时，将卡组最上方 1 张加入生命区最上方
        if (me.LifeCount <= 1)
        {
            AtomicOps.AddLifeFromDeckTop(me, 1);
        }
    }
}
