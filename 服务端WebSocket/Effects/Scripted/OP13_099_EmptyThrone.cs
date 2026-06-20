using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-099 空的王座（舞台，地，7 费，圣地玛丽乔尔）
/// 【我方的回合中】我方废弃区中有 19 张或更多卡牌的场合，我方领袖力量 +1000。
/// 【启动主要】可以将此卡牌和我方的 3 张咚!! 转为休息状态：
///   将我方手牌中最多 1 张费用不高于我方场上咚!!的张数且拥有《五老星》特征的黑色角色卡牌登场。
///
/// 实现说明 / 简化点：
///   - 【我方的回合中】+1000：通过 ContinuousEffect 注册，Predicate 判定「我方回合 且 废弃区 ≥19」
///     时对我方领袖 +1000；来源舞台离场时由引擎按 SourceCardId 清理。
///     注册时机：本卡无【登场时】文本，引擎不会在登场时调用本脚本，故在【启动主要】解析时
///     （即玩家首次使用启动效果时）注册/刷新该持续效果。简化点：在玩家首次使用启动效果前，
///     该 +1000 持续效果尚未生效。
///   - 【启动主要】成本 = 将此舞台转为休息 + 将我方 3 张活跃咚转为休息（活跃咚不足 3 张或舞台已休息则无法发动）。
///   - 效果候选 = 手牌中「拥有《五老星》特征、黑色（含元素色〈暗〉）、费用 ≤ 我方场上咚!!总张数」的角色卡。
///     “场上咚!!的张数”取费用区咚!!总数 TotalDonInCostArea（含活跃/休息/被赋予中）。
///     “最多 1 张”→ min=0、max=1；免费登场（PlayFromHandFree）。
///     手牌身份对客户端默认不可见，故传 extra.choiceCards 显示卡面。
/// </summary>
public class OP13_099_EmptyThrone : IScriptedEffect
{
    public string CardNumber => "OP13-099";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        int ownerIndex = ctx.OwnerIndex;
        var selfId = ctx.Source.Id;

        // ── 注册/刷新持续：我方回合 且 废弃区 ≥19 → 我方领袖 +1000 ──
        s.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        s.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            PowerDelta = 1000,
            Predicate = (st, sideIdx, card) =>
                card == st.Players[ownerIndex].Leader &&
                st.CurrentTurnPlayer == ownerIndex &&
                st.Players[ownerIndex].Trash.Count >= 19,
        });

        // ── 【启动主要】成本前置：此舞台须活跃 且 活跃咚 ≥3 ──
        if (ctx.Source.IsTapped) return;
        var activeDons = me.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (activeDons.Count < 3) return;

        // 效果候选：手牌中《五老星》黑色（含〈暗〉）角色，费用 ≤ 我方场上咚!!总张数
        int fieldDon = me.TotalDonInCostArea;
        var candidates = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character
                        && c.Info.HasKeyword("五老星")
                        && c.Info.ColorList.Contains("紫")
                        && c.Info.Cost <= fieldDon)
            .ToList();
        if (candidates.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            $"将此舞台与 3 张咚!! 转为休息：将手牌中最多 1 张费用不高于 {fieldDon} 且拥有《五老星》特征的黑色角色登场",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        // 支付成本：此舞台转休息 + 3 张活跃咚转休息
        ctx.Source.IsTapped = true;
        for (int i = 0; i < 3; i++) activeDons[i].State = DonState.Rest;

        var card = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(s, ctx.OwnerIndex, card);
    }
}
