using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-051 安普里奥·伊万科夫（角色 / 水 cost7）
/// 【登场时】抽取卡牌直到我方手牌变为 3 张，将我方手牌中最多 1 张费用不高于 6 且
///   拥有《因佩尔地狱》特征的蓝色角色卡牌登场。
///
/// 实现说明：
///   - "抽到手牌变为 3 张"=补抽到 3 张：need = 3 - 当前手牌数，need>0 时抽 need 张（手牌已≥3 则不抽）。
///   - 之后从手牌登场最多 1 张 cost≤6、《因佩尔地狱》且蓝色(水)的角色。
/// </summary>
public class OP02_051_Ivankov : IScriptedEffect
{
    public string CardNumber => "OP02-051";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        int need = 3 - me.Hand.Count;
        if (need > 0) AtomicOps.Draw(ctx.State, owner, need);

        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 6 &&
            c.Info.HasKeyword("因佩尔地狱") &&
            c.Info.ColorList.Contains("蓝")).ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场最多 1 张 费用≤6 的蓝色《因佩尔地狱》角色",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, owner, picked);
        }
    }
}
