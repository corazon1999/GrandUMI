using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-053 瓦斯蒂诺（角色 / 光 / 科学家）
/// 【登场时】将对方最多 1 张费用不高于 3 的角色正面朝上放置到其生命区的最上方或最下方。
///
/// 实现说明：
///   - 候选：对方原本费用≤3 的角色。
///   - 选 1 张后再询问放到对方生命区最上方还是最下方，用 MoveCharToLife（generic ownerIdx 适用于对方）。
///   - 【触发】另由触发系统处理，本脚本仅实现【登场时】。
/// </summary>
public class EB01_053_Wapister : IScriptedEffect
{
    public string CardNumber => "EB01-053";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        var cands = opp.Characters.Where(c => c.Info.Cost <= 3).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择最多 1 张费用≤3 的对方角色，放入其生命区",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);

        int pos = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将该角色放置到对方生命区的？", new[] { "最上方", "最下方" });

        AtomicOps.MoveCharToLife(ctx.State, oppIdx, tgt, toTop: pos == 0);
    }
}
