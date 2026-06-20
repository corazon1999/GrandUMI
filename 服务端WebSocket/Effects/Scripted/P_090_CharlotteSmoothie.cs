using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-090 夏洛特·果昔（角色 / 暗）
/// 【对方的回合中】【KO时】咚!!-1：将我方手牌中最多1张“夏洛特·果昔”以外的、
///   费用不高于对方场上咚!!的张数、且拥有《大妈海盗团》特征的角色卡牌登场。
///
/// 实现说明：
///   - 【KO时】= OnKO 触发；额外限定为【对方的回合中】(CurrentTurnPlayer != OwnerIndex)。
///   - 费用上限为动态值「对方场上咚!!张数」= opp.TotalDonInCostArea，在脚本内即时计算。
///   - 成本咚!!-1，可选发动用 ConfirmOptional。
/// </summary>
public class P_090_CharlotteSmoothie : IScriptedEffect
{
    public string CardNumber => "P-090";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 仅【对方的回合中】发动
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;

        // 成本前置：费用区需要至少1张可放回的咚（活跃/休息/附着皆可）
        if (me.CostArea.Count < 1) return;

        int costCap = opp.TotalDonInCostArea;
        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            !c.Info.NameContains("夏洛特·果昔") &&
            c.Info.Cost <= costCap &&
            c.Info.HasKeyword("大妈海盗团")).ToList();
        if (playable.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"夏洛特·果昔【KO时】：咚!!-1，登场1张费用≤{costCap}的《大妈海盗团》角色？");
        if (!use) return;

        // 成本：咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            $"登场1张费用≤{costCap}的《大妈海盗团》角色（最多1张）",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
