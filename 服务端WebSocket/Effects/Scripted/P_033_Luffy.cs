using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-033 蒙奇·D·路飞（角色 / 水 / 草帽一伙，cost4 power5000）
/// 【启动主要】可以将此角色放回持有者的卡组最下方：抽取 1 张卡牌。
///
/// 实现说明：
///   - 成本"将自身放回卡组最下方"用 ReturnFieldToDeckBottom；"可以"=可选先 ConfirmOptional。
///   - 支付成本后抽 1 张。
/// </summary>
public class P_033_Luffy : IScriptedEffect
{
    public string CardNumber => "P-033";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞【启动主要】：将此角色放回卡组最下方，抽取 1 张卡牌？");
        if (!use) return;

        // 成本：将自身放回卡组最下方
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, ctx.Source);

        // 收益：抽 1 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
    }
}
