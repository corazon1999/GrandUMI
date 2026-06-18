using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-044 克里埃尔（角色）
/// 【攻击时】赋予我方拥有的特征中包含〈白胡子海盗团〉的领袖，或 1 张拥有的特征中包含
///   〈白胡子海盗团〉的角色，最多 1 张休息状态的咚!!。
/// 【KO时】抽取 1 张卡牌。
/// </summary>
public class OP13_044_Crierl : IScriptedEffect
{
    public string CardNumber => "OP13-044";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnAttackDeclare || t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            // 【KO时】抽取 1 张
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        // ── 【攻击时】 ──
        // 候选：领袖（若含〈白胡子海盗团〉特征）+ 含该特征的角色
        var candidates = new List<CardInstance>();
        if (me.Leader.Info.HasKeyword("白胡子海盗团")) candidates.Add(me.Leader);
        candidates.AddRange(me.Characters.Where(c => c.Info.HasKeyword("白胡子海盗团")));
        if (candidates.Count == 0) return;

        // 需要有休息状态的咚可赋予
        int restDon = me.CostArea.Count(d => d.State == DonState.Rest);
        if (restDon <= 0) return;

        // “最多 1 张” → 可选（min=0）
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacterWhitebeard",
            "赋予含〈白胡子海盗团〉的领袖或 1 张角色 1 张休息状态的咚!!（可不选）",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Rest);
    }
}
