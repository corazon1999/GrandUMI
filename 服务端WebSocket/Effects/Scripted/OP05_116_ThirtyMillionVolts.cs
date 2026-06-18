using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-116 3000万伏 雷鸟（事件 / 光 2 费）
/// 【主要】将对方最多 1 张费用不高于对方生命卡牌张数的角色 KO。
///   （卡面【触发】为"发动此卡牌的【主要】效果"，触发节单独处理，不在本脚本内。）
///
/// 实现说明：
///   - 阈值为动态值：对方生命卡牌张数 = opp.LifeArea.Count。
///   - 候选为对方角色中费用 ≤ 该阈值者，玩家选最多 1 张 KO。
/// </summary>
public class OP05_116_ThirtyMillionVolts : IScriptedEffect
{
    public string CardNumber => "OP05-116";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        int threshold = opp.LifeArea.Count;
        var cands = opp.Characters.Where(c => c.Info.Cost <= threshold).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            $"选择最多 1 张费用≤{threshold} 的对方角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
