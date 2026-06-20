using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-094 盖德（角色 / 暗 / 四皇・百兽海盗团，cost10 power12000）
/// 【登场时】咚!!-6（可以将我方场上指定数量的咚‼放回咚‼卡组）：
///   我方领袖拥有《百兽海盗团》特征的场合，将此角色以外的所有角色KO。
///
/// 实现说明：
///   - 成本 咚!!-6 为可选（"可以将…放回"）：ConfirmOptional 后 ReturnDonToDeck(6)。
///   - 仅当我方领袖拥有《百兽海盗团》特征时产生收益，故仅在该前提下询问发动。
///   - "此角色以外的所有角色KO"：遍历双方场上角色，排除自身后逐张 KO（按目标所属方下标）。
/// </summary>
public class OP01_094_Kaido : IScriptedEffect
{
    public string CardNumber => "OP01-094";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 仅当领袖拥有《百兽海盗团》特征时有收益
        if (!me.Leader.Info.HasKeyword("百兽海盗团")) return;

        // 需可支付咚!!-6
        if (me.CostArea.Count < 6) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "盖德【登场时】：将6张咚!!放回咚!!卡组，将此角色以外的所有角色KO？");
        if (!use) return;

        // 成本：咚!!-6
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 6)) return;

        // 将此角色以外的双方所有角色 KO
        var selfId = ctx.Source.Id;
        foreach (var c in me.Characters.Where(c => c.Id != selfId).ToList())
            AtomicOps.KO(ctx.State, ctx.OwnerIndex, c);
        foreach (var c in ctx.State.Players[1 - ctx.OwnerIndex].Characters.ToList())
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, c);
    }
}
