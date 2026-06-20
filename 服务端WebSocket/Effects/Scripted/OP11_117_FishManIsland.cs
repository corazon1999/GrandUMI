using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-117 鱼人岛（舞台 / 光 2 费）
/// 【启动主要】【每回合1次】我方领袖为"白星"的场合，可以将我方生命区最上方的1张卡牌翻至正面朝上：
///   本回合中，我方最多1张拥有《海王类》或《鱼人族》或《人鱼族》特征的角色力量+1000。
///
/// 实现说明 / 简化点：
///   - 启动条件：领袖名含"白星"。
///   - 成本"将生命区最上方1张翻至正面朝上"：引擎不跟踪生命牌正反面状态，故无可表达的状态变化，
///     按惯例只实现收益（仍要求生命区非空作为可支付前提）。
///   - 收益：对我方 1 张含《海王类》/《鱼人族》/《人鱼族》特征的角色本回合 +1000。
///   - 每回合1次：用 me.TurnOnceUsed 防重复。
/// </summary>
public class OP11_117_FishManIsland : IScriptedEffect
{
    public string CardNumber => "OP11-117";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 启动条件：领袖为"白星"
        if (!me.Leader.Info.NameContains("白星")) return;

        // 成本前提：生命区非空
        if (me.LifeArea.Count == 0) return;

        var targets = me.Characters
            .Where(c => c.Info.HasKeyword("海王类") || c.Info.HasKeyword("鱼人族") || c.Info.HasKeyword("人鱼族"))
            .ToList();
        if (targets.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "鱼人岛【启动主要】：将生命区最上方 1 张翻至正面朝上，使我方 1 张《海王类》/《鱼人族》/《人鱼族》角色本回合 +1000？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);

        // 成本：将生命区最上方 1 张翻至正面朝上（引擎不跟踪生命正反面，无状态变化）

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张《海王类》/《鱼人族》/《人鱼族》角色，本回合 +1000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddPowerThisTurn(tgt, 1000);
    }
}
