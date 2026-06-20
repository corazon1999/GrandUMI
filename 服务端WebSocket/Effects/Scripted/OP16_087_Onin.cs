using System.Linq;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-087 阿忍（角色，和之国，力量1000）
/// 【登场时】可以将此角色放置到废弃区：我方领袖拥有《和之国》特征的场合，抽取1张卡牌，
///   本回合中，我方最多1张"光月桃之助"的费用+20。
///
/// 实现：
///   - 可选成本：确认后将自身放置废弃区（BattleEngine.KOCard，不触发 KO 事件，同 DSL selfToTrash）。
///   - 领袖《和之国》才有收益：抽1 + 选我方场上1张"光月桃之助" CostModThisTurn+=20（仅场上，反馈#133）。
///   - +20 用于启用 OP16-084 桃之助的【启动主要】(需费用≥20)。场上卡的 CostModThisTurn 回合末由 TurnEngine 清。
/// </summary>
public class OP16_087_Onin : IScriptedEffect
{
    public string CardNumber => "OP16-087";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 可选成本：是否将自身放置废弃区
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "阿忍【登场时】：将此角色放置到废弃区？（领袖有《和之国》则抽1，并使我方1张\"光月桃之助\"本回合费用+20）");
        if (!use) return;

        // 支付：自身入废弃（不触发 KO 事件）
        BattleEngine.KOCard(ctx.State, ctx.OwnerIndex, ctx.Source);

        // 领袖《和之国》才有收益
        if (!me.Leader.Info.HasKeyword("和之国")) return;
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

        // 选我方场上1张"光月桃之助"，本回合费用+20（官方原文未含手牌；+20仅对场上角色有意义，见反馈#133）
        var cands = me.Characters.Where(c => c.MatchesName("光月桃之助")).ToList();
        if (cands.Count == 0) return;
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnMomonosuke",
            "选择1张\"光月桃之助\"，本回合其费用+20", cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;
        var picked = cands.First(c => c.Id.ToString() == chosen[0]);
        picked.CostModThisTurn += 20;
    }
}
