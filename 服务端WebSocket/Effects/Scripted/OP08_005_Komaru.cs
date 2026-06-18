using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-005 棋子（角色）
/// 【登场时】本回合中，对方最多 1 张角色力量 -2000。
///   之后，我方场上不存在"黑莫里莫"的场合，将我方手牌中最多 1 张"黑莫里莫"登场。
///
/// 说明 / 简化点：
///   - "之后"分句按顺序门控：先做无条件的 -2000，再判我方场上无"黑莫里莫"才执行登场。
///   - 名称条件用 MatchesName("黑莫里莫")：场上存在性判定 + 手牌候选筛选。
/// </summary>
public class OP08_005_Komaru : IScriptedEffect
{
    public string CardNumber => "OP08-005";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 本回合中，对方最多 1 张角色力量 -2000
        if (opp.Characters.Count > 0)
        {
            var debuff = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "本回合中，对方最多 1 张角色力量 -2000",
                opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (debuff.Count > 0)
            {
                var tgt = opp.Characters.First(c => c.Id.ToString() == debuff[0]);
                AtomicOps.AddPowerThisTurn(tgt, -2000);
            }
        }

        // 之后：我方场上不存在"黑莫里莫"的场合，将手牌中最多 1 张"黑莫里莫"登场
        if (me.Characters.Any(c => c.MatchesName("黑莫里莫"))) return;
        var playable = me.Hand.Where(c => c.Info.Kind == CardKind.Character && c.MatchesName("黑莫里莫")).ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将我方手牌中最多 1 张\"黑莫里莫\"登场",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
