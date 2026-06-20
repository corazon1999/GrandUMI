using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-054 马歇尔·D·提奇（王下七武海/黑胡子海盗团）
/// 【登场时】我方领袖拥有《王下七武海》特征的场合，
/// 将此角色以外最多 1 张费用不高于 1 的角色放回其持有者的手牌。
///
/// 说明：
///   - 条件：我方领袖须带《王下七武海》特征，否则不发动。
///   - 候选：双方场上、费用不高于 1、且不是此角色自身的角色。
///   - "最多 1 张" 为可选（min=0）。
/// </summary>
public class OP12_054_Teach : IScriptedEffect
{
    public string CardNumber => "OP12-054";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        // 条件：我方领袖须拥有《王下七武海》特征
        if (!me.Leader.Info.HasKeyword("王下七武海")) return;

        // 候选：双方场上，费用不高于 1，且不是此角色自身
        var candidates = new List<(CardInstance card, int owner)>();
        for (int i = 0; i < 2; i++)
        {
            foreach (var c in s.Players[i].Characters)
                if (c.Info.Cost <= 1 && c.Id != ctx.Source.Id)
                    candidates.Add((c, i));
        }
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Teach054Bounce",
            "将此角色以外最多 1 张费用不高于 1 的角色放回其持有者的手牌",
            candidates.Select(c => c.card.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var pick = candidates.First(c => c.card.Id.ToString() == chosen[0]);
        AtomicOps.BounceToHand(s, pick.owner, pick.card);
    }
}
