using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-056 蒙奇·D·戈普（海军）
/// 【登场时】可以丢弃我方的 1 张手牌：将我方手牌中最多 1 张
/// "蒙奇·D·戈普"以外、力量不高于 8000 且拥有《海军》特征的蓝色角色卡牌登场。
///
/// 说明：
///   - "可以丢弃手牌" 为可选成本：先询问是否发动（ConfirmOptional），同意后弃 1 张手牌。
///   - 之后从手牌中符合条件（蓝=水色 / 海军 / 力量≤8000 / 非"蒙奇·D·戈普" / 角色卡）
///     的卡里选最多 1 张免费登场（可选，min=0）。
///   - 弃牌与登场目标均经 extra.choiceCards 下发卡面给客户端。
/// </summary>
public class OP12_056_Garp : IScriptedEffect
{
    public string CardNumber => "OP12-056";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        // 成本：可以丢弃 1 张手牌。手牌为空则无法支付，效果不发动
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "戈普：丢弃我方 1 张手牌，将手牌中 1 张力量≤8000 的蓝色《海军》角色登场？");
        if (!use) return;

        // 选择并丢弃 1 张手牌作为成本
        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var dchosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Garp056Discard",
            "丢弃 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, discardExtra);
        var discard = dchosen.Count > 0
            ? me.Hand.FirstOrDefault(c => c.Id.ToString() == dchosen[0])
            : me.Hand[0];
        if (discard is null) return;
        AtomicOps.DiscardHand(me, discard);

        // 候选：手牌中"蒙奇·D·戈普"以外、力量≤8000、《海军》、蓝(水)色的角色卡
        var candidates = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.ColorList.Contains("蓝") &&
            c.Info.HasKeyword("海军") &&
            c.Info.Power <= 8000 &&
            !c.MatchesName("蒙奇·D·戈普")).ToList();
        if (candidates.Count == 0) return;

        var playExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var pchosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Garp056Play",
            "将最多 1 张力量≤8000 的蓝色《海军》角色登场",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, playExtra);
        if (pchosen.Count == 0) return;

        var pick = candidates.First(c => c.Id.ToString() == pchosen[0]);
        AtomicOps.PlayFromHandFree(s, ctx.OwnerIndex, pick);
    }
}
