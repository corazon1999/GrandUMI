using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-024 闪电（角色 / 风 / 因佩尔地狱・革命军，cost2 power1000）
/// 当此角色因对方的效果而被KO时，将对方最多 1 张角色转为休息状态。
/// 【阻挡者】（关键词，引擎处理）
///
/// 实现说明（效果KO来源判定）：
///   - 走 OnKO；引擎的效果KO（KOByEffectAsync）会设 s.KOReason="effect"、s.KOActingSide=发起方，
///     据此判定"因对方的效果"。战斗KO时 KOReason 非 "effect"，正确地不发动。
///   - 收益：从对方角色中选最多 1 张转为休息状态（RestCard）。
///   - 局限：脚本直接同步KO（非DSL）不派发 OnKO，本卡此情形不发动（已文档化）。
/// </summary>
public class OP16_024_Kuzan : IScriptedEffect
{
    public string CardNumber => "OP16-024";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        // 仅"因对方的效果而被KO"
        if (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex) return;

        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var cands = opp.Characters.Where(c => !c.IsTapped).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
