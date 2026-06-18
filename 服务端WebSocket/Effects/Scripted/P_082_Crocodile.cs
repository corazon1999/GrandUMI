using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-082 克洛克达尔（角色 / 水 / 十字公会·原巴洛克工作室）
/// 【我方的回合中】【登场时】我方领袖拥有《十字公会》特征或拥有的特征中包含＜巴洛克工作室＞的场合，
///   将对方最多 1 张力量不高于 2000 的角色放回其持有者的卡组最下方。
///
/// 实现说明：领袖条件为两特征 OR（HasKeyword OR），选对方力量≤2000角色放回卡组底。
/// </summary>
public class P_082_Crocodile : IScriptedEffect
{
    public string CardNumber => "P-082";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        // 【我方的回合中】
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        // 领袖条件：拥有《十字公会》特征 或 拥有＜巴洛克工作室＞特征
        bool leaderOk = me.Leader.Info.HasKeyword("十字公会") || me.Leader.Info.HasKeyword("巴洛克工作室");
        if (!leaderOk) return;

        var cands = opp.Characters.Where(c => ctx.State.CurrentPowerOf(oppIdx, c) <= 2000).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张力量≤2000 的角色放回卡组最下方",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;
        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, oppIdx, tgt);
    }
}
