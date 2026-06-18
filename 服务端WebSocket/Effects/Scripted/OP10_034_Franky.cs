using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-034 弗兰奇（角色 / 风 4 费 5000，时光旅诗·草帽一伙）
/// 【每回合1次】此角色因战斗将要被KO的场合，可以改为将我方生命区最上方的1张卡牌加入手牌，使此角色不会被KO。
///
/// 实现：
///   - 战斗KO置换守护，用 OnAllyWillBeKOd（仅战斗KO派发，与文本"因战斗"一致）。
///   - 仅当被KO的是本卡自身时触发；每回合1次。
///   - 置换成本：将我方生命区最上方1张卡加入手牌；之后 MarkPreventKO 取消本次KO。
/// </summary>
public class OP10_034_Franky : IScriptedEffect
{
    public string CardNumber => "OP10-034";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillBeKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-guard" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        if (victimId is null) return;
        // 仅在被KO的是本卡自身时触发
        if (victimId != self.Id.ToString()) return;

        // 置换成本：需要生命区至少有1张卡
        if (me.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "弗兰奇【每回合1次】：将我方生命区最上方1张卡加入手牌，使此角色不会被KO？");
        if (!use) return;

        // 将生命区最上方1张卡加入手牌
        var top = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        me.Hand.Add(top);

        ctx.State.MarkPreventKO(self.Id);
        me.TurnOnceUsed.Add(key);
    }
}
