using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-035 霍迪·琼斯（角色）
/// 【速攻】（关键词，由引擎处理，无需脚本）
/// 【登场时】将对方合计最多 2 张角色或咚!! 转为休息状态。之后，将我方生命区最上方的 1 张卡牌加入手牌。
///
/// 实现说明 / 简化点：
///   - "对方合计最多 2 张角色或咚!! 转为休息状态"：先让玩家选最多 2 张对方角色横置；
///     若不足 2 张，再从对方活跃咚!! 中补足横置（RestCard）。咚!! 不便单张点选，故角色优先、
///     剩余名额自动横置对方活跃咚!!（取实战常见处理）。
///   - "生命区最上方 1 张加入手牌"：生命牌私有，手动 RemoveAt(0) 后加入手牌。
/// </summary>
public class OP06_035_Hody_Jones : IScriptedEffect
{
    public string CardNumber => "OP06-035";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        int budget = 2;

        // 先横置对方角色（玩家选择，最多 2 张活跃角色）
        var charCands = opp.Characters.Where(c => !c.IsTapped).ToList();
        if (charCands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方合计最多 2 张角色或咚!! 转为休息状态：选择对方角色（之后不足名额自动横置对方咚!!）",
                charCands.Select(c => c.Id.ToString()).ToList(), 0, Math.Min(budget, charCands.Count));
            foreach (var id in chosen)
            {
                var tgt = charCands.First(c => c.Id.ToString() == id);
                AtomicOps.RestCard(tgt);
                budget--;
            }
        }

        // 剩余名额横置对方活跃咚!!
        if (budget > 0)
        {
            var activeDons = opp.CostArea.Where(d => d.State == DonState.Active).ToList();
            int n = Math.Min(budget, activeDons.Count);
            for (int i = 0; i < n; i++)
                activeDons[i].State = DonState.Rest;
        }

        // 生命区最上方 1 张加入手牌
        if (me.LifeArea.Count > 0)
        {
            var top = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            me.Hand.Add(top);
        }
    }
}
