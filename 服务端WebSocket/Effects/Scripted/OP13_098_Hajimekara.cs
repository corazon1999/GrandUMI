using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-098 打从一开始……就不存在吧……（事件，地）
/// 【主要】可以将我方的 1 张咚!! 转为休息状态：我方领袖为“伊姆”的场合，
///           将对方最多 1 张费用为 7 的舞台 KO。
/// 【反击】我方领袖为“伊姆”的场合，本次战斗中，我方最多 1 张领袖或角色力量 +4000。
///
/// 实现说明 / 简化点：
///   - 【主要】可选成本 = 将我方 1 张活跃咚转为休息状态（活跃咚不足 1 张则无法发动）。
///     条件 = 我方领袖为“伊姆”。效果 = 将对方场上 1 张费用为 7 的舞台 KO（“最多 1 张”→ min=0）。
///     现有 DSL Choose 候选种类无“对方舞台”，故走脚本自建候选。
///   - 【反击】= 领袖为“伊姆”时，我方最多 1 张领袖或角色本次战斗力量 +4000（min=0）。
///   - 舞台 KO 走 BattleEngine.KOCard（与 AtomicOps.KO 一致）。
/// </summary>
public class OP13_098_Hajimekara : IScriptedEffect
{
    public string CardNumber => "OP13-098";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = s.Players[oppIdx];

        // ── 【反击】 ──
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            // 条件：我方领袖为“伊姆”
            if (!me.Leader.Info.NameIs("伊姆")) return;

            var targets = new List<CardInstance> { me.Leader };
            targets.AddRange(me.Characters);
            var picked = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "选我方最多 1 张领袖或角色，本次战斗力量 +4000",
                targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (picked.Count == 0) return;
            var tgt = targets.FirstOrDefault(c => c.Id.ToString() == picked[0]);
            if (tgt is not null) AtomicOps.AddPowerThisBattle(tgt, 4000);
            return;
        }

        // ── 【主要】 ──
        // 可选成本：将我方 1 张活跃咚转为休息状态
        var activeDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (activeDon is null) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "OP13-098【主要】：将我方 1 张咚!! 转为休息状态，若我方领袖为“伊姆”则将对方最多 1 张费用为 7 的舞台 KO？");
        if (!use) return;

        // 支付成本
        activeDon.State = DonState.Rest;

        // 条件：我方领袖为“伊姆”
        if (!me.Leader.Info.NameIs("伊姆")) return;

        // 效果：将对方费用为 7 的舞台 KO（最多 1 张）
        if (opp.StageCard is null || opp.StageCard.Info.Cost != 7) return;

        var stage = opp.StageCard;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentStage",
            "将对方最多 1 张费用为 7 的舞台 KO",
            new List<string> { stage.Id.ToString() }, 0, 1);
        if (chosen.Count == 0) return;

        AtomicOps.KO(s, oppIdx, stage);
    }
}
