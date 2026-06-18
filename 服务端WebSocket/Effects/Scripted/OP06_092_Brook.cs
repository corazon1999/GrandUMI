using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-092 布鲁克（角色）
/// 【登场时】选择以下的 1 项：
///   ・将对方最多 1 张费用不高于 4 的角色放置到废弃区（KO）。
///   ・对方将其废弃区中的 3 张卡牌自选顺序放回其卡组最下方。
///
/// 实现说明 / 简化点：
///   - 模态二选一用 ChooseOption。
///   - 第二项"对方自选顺序放回卡组底"由系统自动取对方废弃区任意 3 张按原相对顺序放底
///     （对手收益型操作不弹交互，"自选顺序"对实战影响极小）。
/// </summary>
public class OP06_092_Brook : IScriptedEffect
{
    public string CardNumber => "OP06-092";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择以下的 1 项",
            new[] { "将对方 1 张费用≤4 的角色 KO", "对方将其废弃区 3 张放回卡组最下方" });

        if (opt == 0)
        {
            var cands = opp.Characters.Where(c => c.Info.Cost <= 4).ToList();
            if (cands.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张费用≤4 的对方角色 KO", cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }
        else
        {
            // 对方废弃区 3 张按原相对顺序放回卡组最下方
            var move = opp.Trash.Take(3).ToList();
            foreach (var c in move) AtomicOps.ReturnTrashToDeckBottom(opp, c);
        }
    }
}
