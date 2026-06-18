using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-004 黑莫里莫（角色）
/// 【登场时】我方场上存在"棋子"的场合，将对方最多 1 张力量不高于 3000 的角色 KO。
///
/// 说明：
///   - 条件"我方场上存在棋子"用 me.Characters.Any(c => c.MatchesName("棋子"))。
///   - KO 对方角色用 AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt)。
/// </summary>
public class OP08_004_KuroMorimo : IScriptedEffect
{
    public string CardNumber => "OP08-004";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方场上存在"棋子"
        if (!me.Characters.Any(c => c.MatchesName("棋子"))) return;

        var cands = opp.Characters.Where(c => ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, c) <= 3000).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张力量≤3000 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
