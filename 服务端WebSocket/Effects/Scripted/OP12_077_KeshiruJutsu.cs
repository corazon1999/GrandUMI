using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-077 这是『消除你所引发的所有声音之术』（事件）
/// 【主要】选择我方最多 1 张"特拉法尔加·罗"，本回合中，力量+2000。
///   之后，本回合中，当选择的卡牌将要攻击时，对方无法发动【阻挡者】效果。
/// 【触发】抽取 1 张卡牌。
///
/// 实现说明：
/// - "本回合中，当选择的卡牌将要攻击时，对方无法发动【阻挡者】"——
///   通过本回合给该卡赋予【不可阻挡】实现：阻挡宣言校验
///   (ActionValidator.CanDeclareBlocker) 会在攻击者具有【不可阻挡】时拒绝阻挡。
///   效果等价于"该卡攻击时对方无法发动【阻挡者】"。
/// - 可选目标为我方场上所有名为"特拉法尔加·罗"的角色。无对象时该效果跳过。
/// </summary>
public class OP12_077_KeshiruJutsu : IScriptedEffect
{
    public string CardNumber => "OP12-077";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 【触发】抽取 1 张卡牌
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        // ── 【主要】 ──
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 选择我方最多 1 张"特拉法尔加·罗"
        var candidates = me.Characters.Where(c => c.MatchesName("特拉法尔加·罗")).ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方最多 1 张\"特拉法尔加·罗\"，本回合力量+2000，且其攻击时对方无法发动【阻挡者】",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);

        // 本回合力量+2000
        AtomicOps.AddPowerThisTurn(target, 2000);

        // 本回合该卡攻击时对方无法发动【阻挡者】（等价于本回合获得【不可阻挡】）
        AtomicOps.GiveKeyword(target, "不可阻挡", KeywordDuration.ThisTurn);
    }
}
