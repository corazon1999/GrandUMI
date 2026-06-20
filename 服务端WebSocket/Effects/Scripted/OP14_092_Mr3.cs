using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-092 Mr.3(加尔迪诺)（角色 / 地 / 4 费 / 6000 / 巴洛克工作室）
/// 【对方的回合中】【每回合1次】此角色将要被 KO 的场合，可以改为将我方废弃区中的 3 张卡牌
///   自选顺序放回卡组最下方，使此角色不会被 KO。
///
/// 实现说明 / 简化点：
///   - 用 PreKO 钩子；仅在对方回合中(CurrentTurnPlayer != OwnerIndex)且每回合1次时可发动。
///   - 成本：将我方废弃区 3 张卡牌放回卡组最下方(需废弃区≥3)。可选(ConfirmOptional)。
///   - "自选顺序放回卡组最下方"简化为玩家选择 3 张，按选择顺序放底(对实战影响极小)。
///   - 支付成本后调用 MarkPreventKO 取消本次对自身的 KO。
/// </summary>
public class OP14_092_Mr3 : IScriptedEffect
{
    public string CardNumber => "OP14-092";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.PreKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 仅【对方的回合中】
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;

        // 每回合1次
        var key = self.Info.Number + "-prevent" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本：废弃区需≥3张
        if (me.Trash.Count < 3) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "Mr.3：将我方废弃区 3 张卡牌放回卡组最下方，使此角色不会被 KO？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrash",
            "选择我方废弃区 3 张卡牌，自选顺序放回卡组最下方",
            me.Trash.Select(c => c.Id.ToString()).ToList(), 3, 3);
        if (chosen.Count < 3) return;

        // 支付成本：按选择顺序放回卡组最下方
        foreach (var id in chosen)
        {
            var c = me.Trash.FirstOrDefault(x => x.Id.ToString() == id);
            if (c != null) AtomicOps.ReturnTrashToDeckBottom(me, c);
        }

        me.TurnOnceUsed.Add(key);
        ctx.State.MarkPreventKO(self.Id);
    }
}
