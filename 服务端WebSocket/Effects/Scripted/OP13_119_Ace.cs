using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-119 波特卡斯·D·艾斯（水 / 白胡子海盗团 / 6 费 7000）
///
/// 完整文本：
///   我方生命卡牌不多于 3 张的场合，此角色获得【速攻】效果。
///   【登场时】赋予我方领袖最多 1 张休息状态的咚!!。
///     之后，可以将对方最多 1 张费用不高于 5 的角色放回其持有者的手牌。
///     那样做的场合，对方将其手牌中最多 1 张费用不高于 4 的角色卡牌登场。
///
/// 本脚本实现【登场时】部分：
///   1. 赋予我方领袖最多 1 张休息状态的咚（从费用区取休息咚附给领袖；无休息咚则尝试取活跃咚后转休息态）。
///   2. 可选：将对方 1 张原费用 ≤5 的角色放回其持有者手牌（min=0 可跳过）。
///   3. 若执行了步骤 2，则由对方从其手牌中选最多 1 张原费用 ≤4 的角色登场（对方决定，min=0）。
///      手牌候选经 extra.choiceCards 下发卡面给对方客户端。
///
/// 简化点 / 未实现：
///   - "生命 ≤3 时获得【速攻】" 为条件型持续被动（需引擎在判定速攻资格时按生命数动态求值），
///     当前脚本不处理该被动；仅实现【登场时】。
///   - "费用不高于 N" 按卡牌原本印刷费用（Info.Cost）判定。
/// </summary>
public class OP13_119_Ace : IScriptedEffect
{
    public string CardNumber => "OP13-119";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var oppIndex = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIndex];

        // 1. 赋予我方领袖最多 1 张休息状态的咚!!：只取费用区中已休息的咚，无休息咚则不赋予
        //    （不回退取活跃咚，统一「赋予休息咚」语义，见 AttachDonFromCost 与 ST17-004 修复记录）
        AtomicOps.AttachDonFromCost(me, me.Leader.Id, 1, DonState.Rest);

        // 2. 可选：将对方 1 张原费用 ≤5 的角色放回其持有者手牌
        var bounceCandidates = opp.Characters.Where(c => c.Info.Cost <= 5).ToList();
        if (bounceCandidates.Count == 0) return;

        var bounceChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "可以将对方最多 1 张费用≤5 的角色放回其持有者手牌",
            bounceCandidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (bounceChosen.Count == 0) return; // 未放回 → 不触发对方登场

        var bounceTgt = bounceCandidates.First(c => c.Id.ToString() == bounceChosen[0]);
        AtomicOps.BounceToHand(ctx.State, oppIndex, bounceTgt);

        // 3. 对方从其手牌中选最多 1 张原费用 ≤4 的角色登场（由对方决定）
        var oppPlayable = opp.Hand
            .Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 4)
            .ToList();
        if (oppPlayable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = oppPlayable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var playChosen = await ctx.Prompts.ChooseCards(oppIndex, "OwnHandCharacter",
            "可以将你手牌中最多 1 张费用≤4 的角色登场",
            oppPlayable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (playChosen.Count == 0) return;

        var playCard = oppPlayable.First(c => c.Id.ToString() == playChosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, oppIndex, playCard);
    }
}
