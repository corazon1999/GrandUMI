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
///   - KO置换走 PreKO，非KO效果离场走 OnAllyWillLeaveField。
///   - 两条路径共用同一个每回合1次标记和弃牌成本。
/// </summary>
public class OP13_046_Vista : IScriptedEffect
{
    public string CardNumber => "OP13-046";

    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.PreKO or EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        bool nonKoLeave = ctx.Trigger == EffectTrigger.OnAllyWillLeaveField;

        if (nonKoLeave)
        {
            var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
            var victimOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int oi ? oi : -1;
            if (victimOwner != ctx.OwnerIndex || victimId != self.Id.ToString()) return;
        }

        var key = self.Info.Number + "-guard" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 置换成本：丢弃1张含〈白胡子海盗团〉特征的手牌
        var cands = me.Hand.Where(c => c.Info.HasKeyword("白胡子海盗团")).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "比斯塔【每回合1次】：丢弃1张〈白胡子海盗团〉手牌，使此角色不被KO或离场？");
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

        if (nonKoLeave) ctx.State.MarkPreventLeave(self.Id);
        else ctx.State.MarkPreventKO(self.Id);
        me.TurnOnceUsed.Add(key);
    }
}
