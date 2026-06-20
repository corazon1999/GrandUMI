using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-011 迷你梅利二号（舞台 / 炎 / 草帽一伙）
/// 【启动主要】可以将此卡牌转为休息状态，并将我方1张原本的力量为1000的角色放回卡组最下方：抽取1张卡牌。
///
/// 实现说明：
///   - 成本：将此舞台转为休息状态 + 将我方1张原本力量(Info.Power)为1000的角色放回卡组最下方。
///   - 需此舞台处于活跃状态且存在可放回的角色才可发动。
/// </summary>
public class EB01_011_MiniMerryII : IScriptedEffect
{
    public string CardNumber => "EB01-011";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (self.IsTapped) return; // 需活跃才能转为休息

        var cands = me.Characters.Where(c => c.Info.Power == 1000).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "迷你梅利二号【启动主要】：横置此舞台并将我方1张原本力量1000的角色放回卡组最下方，抽1张牌？");
        if (!use) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方1张原本力量1000的角色放回卡组最下方",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count == 0) return;

        // 成本：横置此舞台
        AtomicOps.RestCard(self);
        // 成本：选定角色放回卡组最下方
        var tgt = cands.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, tgt);

        // 效果：抽1张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
    }
}
