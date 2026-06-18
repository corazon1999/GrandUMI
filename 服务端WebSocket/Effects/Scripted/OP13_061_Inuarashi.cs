using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-061 犬岚（3 费 3000，纯毛族/罗杰海盗团，暗）
/// 【登场时】我方场上存在被赋予中的咚!!的场合，从咚!!卡组中追加最多 1 张休息状态的咚!!。
///            之后，将对方最多 1 张费用不高于 1 的角色 KO。
///
/// 实现：
///   - 条件"我方场上存在被赋予中的咚!!" = 我方费用区存在 ≥1 张 Attached 状态的咚。
///   - 满足条件时：从咚卡组追加 1 张休息状态咚（RefreshDonFromDeck，受咚卡组余量/上限约束）。
///   - 之后：从对方原本费用 ≤1 的角色中选最多 1 张 KO（min=0，可不选）。
///   注意：两段效果共享同一个登场条件；条件不满足则两段都不发动。
/// </summary>
public class OP13_061_Inuarashi : IScriptedEffect
{
    public string CardNumber => "OP13-061";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = s.Players[oppIdx];

        // 条件：我方场上存在被赋予中的咚!!
        bool hasAttachedDon = me.CostArea.Count(d => d.State == DonState.Attached) >= 1;
        if (!hasAttachedDon) return;

        // 从咚卡组追加最多 1 张休息状态咚
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);

        // 之后：将对方最多 1 张原本费用 ≤1 的角色 KO
        var candidates = opp.Characters.Where(c => c.Info.Cost <= 1).ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用不高于 1 的角色 KO",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.KO(s, oppIdx, target);
    }
}
