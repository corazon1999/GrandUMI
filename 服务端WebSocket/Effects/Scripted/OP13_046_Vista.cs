using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-046 比斯塔（角色 / 水 / 白胡子海盗团，cost6 power8000）
/// 【双重攻击】（引擎按关键词自动处理）
/// 【每回合1次】此角色将要被KO的场合，或因对方效果将要离开场上的场合，
///   可以改为丢弃我方手牌中1张拥有的特征中包含〈白胡子海盗团〉的卡牌，以代替被KO或离场。
///
/// 实现说明：
///   - 置换防KO用 PreKO 钩子（守护自身）。置换成本：丢弃1张含〈白胡子海盗团〉特征的手牌，MarkPreventKO 取消本次KO。
///   - 每回合1次。
///   - 局限：PreKO 仅覆盖"将要被KO"的场合；"因对方效果将要离开场上"(退回手牌/放回卡组等非KO离场)
///     引擎无对应的"自身离场前置换"钩子，故该分支未实现（仅实现防KO分支）。
/// </summary>
public class OP13_046_Vista : IScriptedEffect
{
    public string CardNumber => "OP13-046";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.PreKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-guard" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 置换成本：丢弃1张含〈白胡子海盗团〉特征的手牌
        var cands = me.Hand.Where(c => c.Info.HasKeyword("白胡子海盗团")).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "比斯塔【每回合1次】：丢弃1张〈白胡子海盗团〉手牌，使此角色不会被KO？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃1张〈白胡子海盗团〉手牌作为置换成本",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (pick.Count == 0) return;

        var disc = cands.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.DiscardHand(me, disc);

        ctx.State.MarkPreventKO(self.Id);
        me.TurnOnceUsed.Add(key);
    }
}
