using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-058 夏洛特·布玲（领航/领袖类）
/// 【攻击时】可以将我方生命区最上方的 2 张卡牌翻至正面朝上：
///   从咚!!卡组中追加最多 1 张休息状态的咚!!。
/// 生命区不足 2 张，或最上方 2 张中已有正面牌时，无法支付翻面成本。
/// </summary>
public class OP08_058_CharlotteBrulee : IScriptedEffect
{
    public string CardNumber => "OP08-058";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.LifeArea.Count < 2 || me.LifeArea.Take(2).Any(card => card.IsLifeFaceUp)) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "夏洛特·布玲【攻击时】：将生命区最上方 2 张翻至正面，从咚!!卡组中追加最多 1 张休息状态的咚!!？");
        if (!use) return;

        foreach (var lifeCard in me.LifeArea.Take(2))
            lifeCard.IsLifeFaceUp = true;
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
    }
}
