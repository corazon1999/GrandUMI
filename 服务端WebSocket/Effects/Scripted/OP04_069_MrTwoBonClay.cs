using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-069 Mr.2·盆·岁末(本萨姆)（角色 / 暗 / 巴洛克工作室）
/// 【对方攻击时】咚!!-1（可以将我方场上指定数量的咚!!放回咚!!卡组）：
///   本回合中，此角色原本的力量变为与对方进行攻击的领袖或角色相同。
///
/// 实现说明：
///   - 可选成本咚-1；之后将此角色本回合的力量设为攻击者当前力量（SetPowerThisTurn）。
///   - 攻击者：CurrentBattle.AttackerCardId 指向对方（AttackerPlayerIndex）的领袖或角色；
///     用 CurrentPowerOf(attackerSide, 攻击者) 取其当前力量作为目标值（近似"原本力量变为相同"）。
/// </summary>
public class OP04_069_MrTwoBonClay : IScriptedEffect
{
    public string CardNumber => "OP04-069";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var b = ctx.State.CurrentBattle;
        if (b is null) return;
        if (me.TotalDonInCostArea < 1) return;

        // 找到攻击者（位于 AttackerPlayerIndex 一方）
        int atkSide = b.AttackerPlayerIndex;
        var atkPlayer = ctx.State.Players[atkSide];
        var attacker = atkPlayer.Leader.Id == b.AttackerCardId
            ? atkPlayer.Leader
            : atkPlayer.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
        if (attacker is null) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "Mr.2【对方攻击时】：咚!!-1，本回合此角色力量变为与攻击者相同？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        int atkPower = ctx.State.CurrentPowerOf(atkSide, attacker);
        bool ownerTurn = ctx.State.CurrentTurnPlayer == ctx.OwnerIndex;
        int donOnSelf = me.AttachedDonCount(self.Id);
        AtomicOps.SetPowerThisTurn(self, atkPower, donOnSelf, ownerTurn);
    }
}
