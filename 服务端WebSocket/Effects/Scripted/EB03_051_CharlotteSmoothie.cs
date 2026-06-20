using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-051 夏洛特·果昔（角色 / 光 / 大妈海盗团，cost3 power3000）
/// 【登场时】我方存在正面朝上的生命卡牌的场合，将对方最多 1 张费用不高于 2 的角色 KO。
///   之后，将我方所有的生命卡牌翻至正面朝下。
///
/// 实现说明（M2 生命牌朝向）：
///   - 条件"存在正面朝上的生命牌"= me.LifeArea.Any(IsLifeFaceUp)（如被居鲁士 EB01-040 翻面）。
///   - 满足则 KO 对方≤2费角色，再 FlipAllLifeFaceDown 将我方所有生命翻回背面。
/// </summary>
public class EB03_051_CharlotteSmoothie : IScriptedEffect
{
    public string CardNumber => "EB03-051";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (!me.LifeArea.Any(c => c.IsLifeFaceUp)) return; // 无正面朝上生命牌 → 不发动

        var cands = opp.Characters.Where(c => c.Info.Cost <= 2).ToList();
        if (cands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacterCostLe2",
                "将对方最多 1 张费用不高于 2 的角色 KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }

        // 之后：将我方所有生命卡牌翻至正面朝下
        AtomicOps.FlipAllLifeFaceDown(me);
    }
}
