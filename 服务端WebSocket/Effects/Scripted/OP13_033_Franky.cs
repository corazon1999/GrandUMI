using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-033 弗兰奇（角色 / 风 / 3 费 / 5000 / FILM・草帽一伙）
/// 【KO时】将对方最多 2 张卡牌转为休息状态。
///
/// 实现：KO 时让玩家从对方角色中选最多 2 张转为休息状态（可不选）。
/// 简化点：候选限定为对方"角色"（领袖在本引擎模型中不参与休息状态切换，
///   且已处于休息状态的角色再选无意义，故仅列出活跃的角色作为有意义目标）。
/// </summary>
public class OP13_033_Franky : IScriptedEffect
{
    public string CardNumber => "OP13-033";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 候选：对方场上未休息的角色（已休息的再选无效果）
        var candidates = opp.Characters.Where(c => !c.IsTapped).ToList();
        if (candidates.Count == 0) return;

        int max = Math.Min(2, candidates.Count);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            $"将对方最多 {max} 张角色转为休息状态（可不选）",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, max);

        foreach (var cid in chosen)
        {
            var target = candidates.FirstOrDefault(c => c.Id.ToString() == cid);
            if (target is not null) AtomicOps.RestCard(target);
        }
    }
}
