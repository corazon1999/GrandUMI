using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-112 佩吉旺（角色 / 暗 / 百兽海盗团，cost4 power5000）
/// 【启动主要】【每回合1次】咚‼-1（可以将我方场上指定数量的咚‼放回咚‼卡组）：
///   本回合中，此角色可以攻击对方处于活跃状态的角色。
///
/// 实现说明：
///   - 启动主要，每回合1次：TurnOnceUsed 标记。
///   - 成本 咚!!-1：ReturnDonToDeck(1)（"可以放回"，发动即支付）。
///   - "本回合可攻击对方活跃角色"：给自身赋予【可攻击活跃】关键词（本回合），
///     ActionValidator 允许其攻击对方活跃角色。
/// </summary>
public class OP01_112_Page_One : IScriptedEffect
{
    public string CardNumber => "OP01-112";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 需可支付咚!!-1
        if (me.CostArea.Count < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "佩吉旺【启动主要】：将1张咚!!放回咚!!卡组，本回合此角色可以攻击对方活跃角色？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);

        // 成本：咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        // 本回合此角色可攻击对方活跃角色
        AtomicOps.GiveKeyword(ctx.Source, "可攻击活跃", KeywordDuration.ThisTurn);
    }
}
