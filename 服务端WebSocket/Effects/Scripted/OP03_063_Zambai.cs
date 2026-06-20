using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-063 赞巴（角色）
/// 【阻挡者】（纯关键词，由引擎处理，此处不实现）
/// 【登场时】咚!!-1（可以将我方场上 1 张咚!! 放回咚!!卡组）：我方领袖拥有《七水之城》特征的场合，抽取 1 张卡牌。
///
/// 说明：咚!!-1 为可选成本（"可以"），先 ConfirmOptional 询问，确认后将 1 张咚放回咚卡组再抽牌。
/// 仅当领袖含《七水之城》时发动有意义，故先判该前提。
/// </summary>
public class OP03_063_Zambai : IScriptedEffect
{
    public string CardNumber => "OP03-063";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 收益前提：领袖拥有《七水之城》特征
        if (!me.Leader.Info.HasKeyword("七水之城")) return;
        // 成本前提：场上至少有 1 张咚可放回
        if (me.CostArea.Count < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "赞巴【登场时】：咚!!-1（放回 1 张咚!!），抽取 1 张卡牌？");
        if (!use) return;

        // 成本：咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        // 效果：抽 1
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
    }
}
