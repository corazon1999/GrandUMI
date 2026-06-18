using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-110 鲛星（角色 / 光 3 费 5000，人鱼族/鱼人岛）
/// 此角色将要被 KO 的场合，可以改为将我方的 1 张"鱼人岛"或领袖"白星"转为休息状态，使此角色不会被 KO。
/// 【登场时】可以将我方生命区最上方或最下方的 1 张卡牌加入手牌：将对方最多 1 张费用不高于 1 的角色 KO。
///
/// 实现范围：本脚本实现【登场时】可选效果。
///   成本：将我方生命区最上方或最下方的 1 张卡牌加入手牌（需生命区非空，二选一上/下）。
///   效果：将对方最多 1 张费用 ≤1 的角色 KO。
///
/// 未实现（complex 部分，已在卡上以注释说明）：
///   - "将要被 KO 时改为休息我方鱼人岛/白星领袖以避免被 KO"为替代/防 KO 机制，
///     引擎无对应的可脚本化通道（PreKO 替代行为），此部分未实现。
/// </summary>
public class OP11_110_Samezvezda : IScriptedEffect
{
    public string CardNumber => "OP11-110";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本：需生命区非空
        if (me.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "鲛星【登场时】：将生命区最上方或最下方 1 张加入手牌，KO 对方最多 1 张费用≤1 的角色？");
        if (!use) return;

        // 二选一：生命区最上方 / 最下方
        int pos;
        if (me.LifeArea.Count == 1)
        {
            pos = 0;
        }
        else
        {
            int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "将生命区哪一张加入手牌？", new[] { "最上方", "最下方" });
            pos = opt == 1 ? me.LifeArea.Count - 1 : 0;
        }

        var card = me.LifeArea[pos];
        me.LifeArea.RemoveAt(pos);
        me.Hand.Add(card);

        // 效果：将对方最多 1 张费用≤1 的角色 KO
        var cands = opp.Characters.Where(c => c.Info.Cost <= 1).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤1 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
