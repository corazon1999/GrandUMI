using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-057 宝藏狂人（角色 / 光 / 宝藏海盗团，cost4 power5000）
/// 【攻击时】可以将我方生命区最上方或最下方的 1 张卡牌加入手牌：
///   将对方最多 1 张费用不高于 3 的角色正面朝上加入其生命区的最上方或最下方。
///
/// 实现说明：
///   - 可选成本「将我方生命顶或底 1 张入手」：先 ConfirmOptional，无生命牌则不可发动。
///     由玩家选择取生命顶/底其一加入手牌。
///   - 收益：对方费用≤3 角色最多 1 张正面朝上加入对方生命区（顶/底由玩家选）。
///     用 MoveCharToLife(state, 对方下标, 角色, toTop) —— 从对方场上移除并插入对方生命区。
/// </summary>
public class EB02_057_TreasureManiac : IScriptedEffect
{
    public string CardNumber => "EB02-057";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.LifeArea.Count == 0) return; // 无生命牌 → 无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "宝藏狂人【攻击时】：将我方生命顶/底 1 张加入手牌，将对方最多 1 张费用≤3 角色加入其生命区？");
        if (!use) return;

        // 成本：我方生命顶或底 1 张入手
        int which = me.LifeArea.Count == 1
            ? 0
            : await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "将哪一张生命牌加入手牌？",
                new[] { "生命区最上方", "生命区最下方" });
        var lifeCard = which == 0 ? me.LifeArea[0] : me.LifeArea[^1];
        me.LifeArea.Remove(lifeCard);
        me.Hand.Add(lifeCard);

        // 收益：对方费用≤3 角色最多 1 张加入对方生命区
        var cands = opp.Characters.Where(c => c.Info.Cost <= 3).ToList();
        if (cands.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤3 的角色加入其生命区", cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;
        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);

        int place = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "加入对方生命区的哪一处？",
            new[] { "最上方", "最下方" });
        AtomicOps.MoveCharToLife(ctx.State, 1 - ctx.OwnerIndex, tgt, toTop: place == 0);
    }
}
