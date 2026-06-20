using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-040 昆因（领航 / 水・光 / 百兽海盗团）
/// 【咚!!×1】【攻击时】我方生命卡牌和手牌合计张数不多于 4 张的场合，抽取 1 张卡牌。
///   我方场上存在费用为 8 或更高的角色的场合，可以将我方卡组最上方的 1 张卡牌加入生命区最上方，
///   以代替抽取 1 张卡牌。
///
/// 实现说明：
///   - 【咚!!×1】= 此角色被赋予中的咚!! ≥1 张时才发动（me.AttachedDonCount）。
///   - 条件：生命牌数 + 手牌数 ≤ 4。
///   - 若场上有费用≥8 的角色，提供二选一：将卡组顶 1 张置入生命区最上方 代替 抽 1 张。
/// </summary>
public class OP04_040_Queen : IScriptedEffect
{
    public string CardNumber => "OP04-040";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】：此角色须有 ≥1 张被赋予中的咚!!
        if (me.AttachedDonCount(self.Id) < 1) return;

        // 条件：生命牌 + 手牌 ≤ 4
        if (me.LifeArea.Count + me.Hand.Count > 4) return;

        // 若场上存在费用≥8 角色，可选择"将卡组顶 1 张加入生命区最上方"代替抽牌
        bool hasBig = me.Characters.Any(c => c.CurrentCost() >= 8);
        if (hasBig && me.Deck.Count > 0)
        {
            int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "选择其一",
                new[] { "抽取 1 张卡牌", "将卡组顶 1 张加入生命区最上方（代替抽牌）" });
            if (opt == 1)
            {
                AtomicOps.AddLifeFromDeckTop(me, 1);
                return;
            }
        }

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
    }
}
