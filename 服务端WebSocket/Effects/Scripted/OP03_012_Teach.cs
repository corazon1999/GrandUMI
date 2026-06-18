using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-012 马歇尔·D·提奇（角色 / 炎 4 费 6000，白胡子海盗团）
/// 【攻击时】可以将我方1张力量4000或更高的红色角色放置到废弃区：
///   抽取1张卡牌。之后，本次战斗中，此角色的力量+1000。
///
/// 实现说明：
///   - 攻击时（OnAttackDeclare），可选成本=废弃我方1张「原始力量≥4000的红色（炎）角色」。
///   - 支付后：抽1张，并本次战斗此角色+1000。
///   - 红色判定用卡面 ColorList.Contains("红")；力量用卡面 Info.Power。
/// </summary>
public class OP03_012_Teach : IScriptedEffect
{
    public string CardNumber => "OP03-012";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var cands = me.Characters.Where(c =>
            c.Info.Power >= 4000 && c.Info.ColorList.Contains("红")).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "提奇【攻击时】：废弃我方1张力量≥4000的红色角色，抽1张牌，并本次战斗此角色+1000？");
        if (!use) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "成本：废弃我方1张力量≥4000的红色角色",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count < 1) return;

        var victim = cands.First(c => c.Id.ToString() == pick[0]);
        me.Characters.Remove(victim);
        me.Trash.Add(victim);

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        AtomicOps.AddPowerThisBattle(self, 1000);
    }
}
