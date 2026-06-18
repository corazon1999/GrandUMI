using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-024 奈菲特·薇薇（角色 / 水 / 阿拉巴斯坦王国，5 费 4000）
/// 【阻挡者】（纯关键词，由引擎处理）
/// 【登场时】将我方手牌中最多 1 张费用不高于 5 且拥有《阿拉巴斯坦王国》或《草帽一伙》特征的
///   角色卡牌登场。之后，本回合中，我方无法在我方场上登场角色卡牌。
///
/// 实现说明 / 简化点：
///   - 【阻挡者】为纯关键词，脚本只实现【登场时】。
///   - 从手牌登场最多 1 张符合条件的角色：PlayFromHandFree。
///   - "之后本回合我方无法登场角色"用 Wave5 通道 NoPlayCharacterThisTurn（回合结束自动清）。
///     无论是否登场了角色，文本均执行该限制，故无条件加入。
/// </summary>
public class EB03_024_NefeltariVivi : IScriptedEffect
{
    public string CardNumber => "EB03-024";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 5 &&
            (c.Info.HasKeyword("阿拉巴斯坦王国") || c.Info.HasKeyword("草帽一伙"))
        ).ToList();

        if (playable.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
                "登场最多 1 张费用≤5 且拥有《阿拉巴斯坦王国》或《草帽一伙》的角色",
                playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = playable.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
            }
        }

        // 之后：本回合中，我方无法在我方场上登场角色卡牌
        ctx.State.NoPlayCharacterThisTurn.Add(ctx.OwnerIndex);
    }
}
