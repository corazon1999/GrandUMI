using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-075 卡雷拉公司（舞台）
/// 【启动主要】可以将此舞台转为休息状态：我方领袖为"阿斯巴古"的场合，
///   从咚!!卡组中追加最多 1 张休息状态的咚!!。
///
/// 实现说明：
///   - 成本：横置此舞台（RestCard）。仅在领袖为"阿斯巴古"且舞台活跃时有意义。
///   - 收益：RefreshDonFromDeck(1, Rest) 追加 1 张休息咚。
/// </summary>
public class OP03_075_GalleyLaCompany : IScriptedEffect
{
    public string CardNumber => "OP03-075";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 舞台须活跃（可横置作为成本）
        if (ctx.Source.IsTapped) return;
        // 仅领袖为"阿斯巴古"时有收益
        if (!me.Leader.Info.NameContains("阿斯巴古")) return;
        if (me.DonDeck.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "卡雷拉公司【启动主要】：横置此舞台，从咚!!卡组追加 1 张休息状态的咚!!？");
        if (!use) return;

        AtomicOps.RestCard(ctx.Source);
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
    }
}
