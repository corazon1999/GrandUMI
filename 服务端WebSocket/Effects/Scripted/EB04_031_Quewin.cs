using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-031 烬（角色 / 暗 / 露娜利亚族・百兽海盗团 / cost6 power7000）
/// 此角色将要被KO的场合，可以改为将我方场上的1张咚!!放回咚!!卡组，使此角色不会被KO。
/// 【启动主要】【每回合1次】我方领袖拥有《百兽海盗团》特征，且我方场上不存在其他角色"烬"的场合，
///   从咚!!卡组中追加最多1张活跃状态的咚!!，再追加最多1张休息状态的咚!!。
///
/// 实现说明：
///   - 置换防KO用 PreKO 钩子：自身将要被KO时，若费用区有可放回的咚!!（活跃/休息），询问是否放回 1 张取消本次KO。
///   - 启动主要每回合1次：条件领袖含《百兽海盗团》且我方场上无其他名为"烬"的角色；
///     满足时 RefreshDonFromDeck 1 活跃 + 1 休息。
/// </summary>
public class EB04_031_Quewin : IScriptedEffect
{
    public string CardNumber => "EB04-031";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.PreKO || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.PreKO)
        {
            if (me.CostArea.Count < 1) return; // 无咚可放回 → 无法置换

            bool useKo = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "烬：将我方场上的 1 张咚!!放回咚!!卡组，使此角色不会被KO？");
            if (!useKo) return;

            if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
            ctx.State.MarkPreventKO(self.Id);
            return;
        }

        // 【启动主要】【每回合1次】
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 条件：领袖含《百兽海盗团》且场上无其他角色"烬"
        if (!me.Leader.Info.HasKeyword("百兽海盗团")) return;
        bool otherQuewin = me.Characters.Any(c => c.Id != self.Id && c.Info.NameContains("烬"));
        if (otherQuewin) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "烬【启动主要】：从咚!!卡组追加最多 1 张活跃咚!!，再追加最多 1 张休息咚!!？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
    }
}
