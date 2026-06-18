using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-040 居鲁士（领航 / 地·光 / 德莱斯罗兹）
/// 【启动主要】【每回合1次】可以将我方生命区最上方的 1 张卡牌翻至正面朝上：
///   将对方最多 1 张费用为 0 的角色 KO。
///
/// 实现说明：
///   - 【启动主要】= ActivatedMain 时机；【每回合1次】用 TurnOnceUsed 去重。
///   - 成本"将生命区最上方 1 张翻至正面朝上"：引擎不跟踪生命牌正/反面状态，作为可选发动开关
///     （沿用 OP11/OP15 系列同类卡的惯例，仅需生命区有牌）。
///   - 收益：KO 对方最多 1 张费用为 0 的角色。
/// </summary>
public class EB01_040_Kyros : IScriptedEffect
{
    public string CardNumber => "EB01-040";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本：生命区需有牌可翻面（M2 已支持朝向，实际把顶张翻为正面朝上）
        if (me.LifeArea.Count == 0) return;

        var cands = opp.Characters.Where(c => c.Info.Cost == 0).ToList();

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "居鲁士【启动主要】：将生命区最上方 1 张翻至正面朝上，将对方最多 1 张费用为 0 的角色 KO？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);
        AtomicOps.FlipTopLifeFaceUp(me);   // 成本：生命区最上方 1 张翻至正面朝上

        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用为 0 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
    }
}
