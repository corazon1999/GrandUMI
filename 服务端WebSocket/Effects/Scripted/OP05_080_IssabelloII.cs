using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-080 伊利撒贝罗Ⅱ世（角色）
/// 【攻击时】【每回合1次】可以将我方废弃区中的 20 张卡牌放回卡组并切洗：
///   本次战斗中，此角色获得【双重攻击】效果，且力量 +10000。
///
/// 实现说明 / 简化点：
///   - 成本"将废弃区 20 张放回卡组并切洗"为可选成本（"可以"）：废弃区不足 20 张则无法发动。
///   - 放回顺序对实战影响极小，按废弃区原相对顺序放回卡组底后整体切洗。
///   - 收益为本次战斗中【双重攻击】+力量+10000，用 KeywordDuration.ThisBattle。
/// </summary>
public class OP05_080_IssabelloII : IScriptedEffect
{
    public string CardNumber => "OP05-080";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合 1 次
        var key = self.Info.Number + "-attack" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 需要废弃区 ≥20 张才能支付成本
        if (me.Trash.Count < 20) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "伊利撒贝罗Ⅱ世【攻击时】：将废弃区 20 张放回卡组并切洗，本次战斗此角色获得【双重攻击】且力量+10000？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);

        // 成本：将废弃区中 20 张放回卡组并切洗
        var toReturn = me.Trash.Take(20).ToList();
        foreach (var c in toReturn) AtomicOps.ReturnTrashToDeckBottom(me, c);
        AtomicOps.Shuffle(ctx.State, me.Deck);

        // 收益：本次战斗中获得【双重攻击】并力量 +10000
        AtomicOps.GiveKeyword(self, "双重攻击", KeywordDuration.ThisBattle);
        AtomicOps.AddPowerThisBattle(self, 10000);
    }
}
