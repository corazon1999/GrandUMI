using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-031 特拉法尔加·罗（风 / FILM·超新星·心脏海盗团 / 6 费 6000）
///
/// 完整文本：
///   我方生命卡牌不多于 1 张的场合，此角色获得【阻挡者】效果。
///   【登场时】可以将我方 1 张角色放回其持有者的手牌：
///     将我方手牌中最多 1 张费用不高于 5 的角色卡牌以休息状态登场。
///
/// 本脚本实现【登场时】部分：
///   - "可以将我方 1 张角色放回手牌" 为可选的发动成本（min=0 可跳过整个效果）。
///     候选包含此角色自身在内的我方全部角色（文本未排除自身）。
///   - 之后从手牌中选最多 1 张费用 ≤5 的角色，以休息状态登场。
///   - 手牌候选经 extra.choiceCards 下发卡面给客户端。
///
/// 简化点 / 未实现：
///   - "生命 ≤1 时获得【阻挡者】" 为条件型持续被动（需引擎在判定阻挡资格时按生命数动态求值），
///     当前脚本不处理该被动；仅实现【登场时】。
/// </summary>
public class OP13_031_Law : IScriptedEffect
{
    public string CardNumber => "OP13-031";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 成本/条件：可以将我方 1 张角色放回手牌（含自身在内）。可选——不选则整个效果不发动。
        if (me.Characters.Count == 0) return;

        var bounceCandidates = me.Characters.ToList();
        var bounceChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "可以将我方 1 张角色放回手牌（之后从手牌登场 1 张费用≤5 的角色）",
            bounceCandidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (bounceChosen.Count == 0) return; // 跳过

        var bounceTgt = bounceCandidates.First(c => c.Id.ToString() == bounceChosen[0]);
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, bounceTgt);

        // 效果：从手牌选最多 1 张费用 ≤5 的角色，以休息状态登场
        var playable = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 5)
            .ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "选最多 1 张费用≤5 的角色，以休息状态登场",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var card = playable.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, card);
        card.IsTapped = true; // 以休息状态登场
    }
}
