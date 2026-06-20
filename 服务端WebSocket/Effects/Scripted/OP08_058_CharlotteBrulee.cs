using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-058 夏洛特·布玲（领航/领袖类）
/// 【攻击时】可以将我方生命区最上方的 2 张卡牌翻至正面朝上：
///   从咚!!卡组中追加最多 1 张休息状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 可选成本"翻生命最上方 2 张至正面朝上"无对应 AtomicOp（引擎无翻面通道），
///     按规范惯例只实现收益部分（从咚!!卡组追加 1 张休息咚!!）。
///   - 用 ConfirmOptional 询问是否发动；确认后用 RefreshDonFromDeck(me, 1, Rest)。
/// </summary>
public class OP08_058_CharlotteBrulee : IScriptedEffect
{
    public string CardNumber => "OP08-058";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "夏洛特·布玲【攻击时】：从咚!!卡组中追加最多 1 张休息状态的咚!!？");
        if (!use) return;

        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
    }
}
