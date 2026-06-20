using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-033 莫里（角色 / 风 / 巨人族・革命军，cost4 power5000）
/// 此角色将要被KO的场合，可以改为将我方 2 张卡牌转为休息状态，使此角色不会被KO。
/// 【不可阻挡】（关键词，引擎处理）
///
/// 实现说明（带代偿成本的被动替换型防KO，走 PreKO）：
///   - PreKO 触发时本卡为将被KO的受害者(ctx.Source)。可选支付成本：将我方 2 张活跃"卡牌"
///     转为休息状态，支付后 MarkPreventKO 取消本次KO。
///   - "卡牌"= 可休置的活跃 领袖 / 角色 / 舞台 / 咚!!（反馈：活跃咚、领袖、场地都应可选，混在同一列表）。
///   - 成本不足（活跃可休置项<2）时无法支付，不发动。
/// </summary>
public class OP16_033_Mole : IScriptedEffect
{
    public string CardNumber => "OP16-033";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.PreKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (AtomicOps.RestableCount(me) < 2) return; // 活跃可休置项(领袖/角色/舞台/咚)不足2，无法支付

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "莫里：将我方 2 张卡牌转为休息状态，使此角色不会被KO？");
        if (!use) return;

        if (!await AtomicOps.PromptRestOwnCards(ctx, 2,
            "将我方 2 张卡牌转为休息状态（成本，可选活跃 领袖/角色/舞台/咚!!）")) return;
        ctx.State.MarkPreventKO(self.Id);
    }
}
