using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-039 橡皮橡皮蛇枪（2 费，事件，风，鱼人岛/超新星/草帽一伙）
/// 【反击】将对方最多 1 张处于休息状态且费用不高于 4 的角色 KO。
/// 【触发】发动此卡牌的【反击】效果。
///
/// 实现：
///   - EventCounter / OnLifeRevealTrigger：从对方处于休息状态且原本费用 ≤4 的角色中
///     选最多 1 张 KO。
/// </summary>
public class OP13_039_GomuSnakeGun : IScriptedEffect
{
    public string CardNumber => "OP13-039";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        // 候选：对方处于休息状态且原本费用 ≤4 的角色
        var candidates = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 4).ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentRestingCharacter",
            "选择最多 1 张对方休息状态且费用不高于 4 的角色 KO",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is not null) AtomicOps.KO(ctx.State, oppIdx, target);
    }
}
