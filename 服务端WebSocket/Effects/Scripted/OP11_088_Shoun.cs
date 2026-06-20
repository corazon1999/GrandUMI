using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-088 肖恩（角色 / 地 / 海军）
/// 【阻挡者】（引擎关键词，自动处理）
/// 【每回合1次】当对方角色攻击时可以发动。该攻击角色拥有属性（斩）的场合，本次战斗中，此角色的力量+5000。
///
/// 实现说明：
///   - 【对方角色攻击时】= OnOppAttackDeclare 时机；攻击者取 ctx.State.CurrentBattle.AttackerCardId。
///   - 仅当攻击者为对方角色（非领袖）且其属性含"斩"时方有收益；此处即据此判定。
///   - 【每回合1次】用 TurnOnceUsed 去重；可选发动用 ConfirmOptional。
///   - 收益：本次战斗中此角色（肖恩自身）力量 +5000，用 AddPowerThisBattle。
/// </summary>
public class OP11_088_Shoun : IScriptedEffect
{
    public string CardNumber => "OP11-088";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        var b = ctx.State.CurrentBattle;
        if (b is null) return;

        // 攻击角色：对方场上与 AttackerCardId 匹配的角色（领袖不计）
        var attacker = opp.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
        if (attacker is null) return;
        if (!attacker.Info.Property.Contains("斩")) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "肖恩【对方的攻击时】：攻击角色拥有属性（斩），本次战斗此角色力量+5000？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);
        AtomicOps.AddPowerThisBattle(ctx.Source, 5000);
    }
}
