using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-115 2亿伏 雷神（事件 / 光 2 费）
/// 【主要】本回合中，我方最多 1 张领袖或角色力量 +3000。
///   之后，我方生命卡牌不多于 1 张的场合，将对方最多 1 张费用不高于 4 的角色转为休息状态。
///
/// 实现说明 / 简化点：
///   - 仅实现【主要】（EventMain）。卡面的【触发】(弃 2 手牌→卡组顶入生命)由触发节单独处理，不在本脚本内。
///   - "本回合中 +3000" 用 AddPowerThisTurn。
///   - "我方生命卡牌不多于 1 张"判定 me.LifeArea.Count <= 1。
///   - "转为休息状态"用 RestCard。
/// </summary>
public class OP05_115_NimillionVolts : IScriptedEffect
{
    public string CardNumber => "OP05-115";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 本回合中，我方最多 1 张领袖或角色力量 +3000
        var buffTargets = new List<CardInstance> { me.Leader };
        buffTargets.AddRange(me.Characters);
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张领袖或角色，本回合力量 +3000",
            buffTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var tgt = buffTargets.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisTurn(tgt, 3000);
        }

        // 之后：我方生命卡牌不多于 1 张时，将对方最多 1 张费用≤4 的角色转为休息状态
        if (me.LifeArea.Count <= 1)
        {
            var cands = opp.Characters.Where(c => c.Info.Cost <= 4).ToList();
            if (cands.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张费用≤4 的对方角色转为休息状态",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.RestCard(tgt);
            }
        }
    }
}
