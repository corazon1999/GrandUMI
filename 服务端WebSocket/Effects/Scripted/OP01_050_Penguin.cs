using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-050 佩金（角色 / 风 / 心脏海盗团）
/// 【阻挡者】（纯关键词，由引擎处理）
/// 【登场时】我方场上不存在"夏齐"的场合，将我方手牌中最多 1 张"夏齐"登场。
///
/// 实现说明：
///   - 仅实现【登场时】主动效果（阻挡者为关键词，引擎处理）。
///   - 条件：我方场上（角色区）不存在名为"夏齐"的角色。
///   - 从手牌选最多 1 张名为"夏齐"的角色用 PlayFromHandFree 登场。
/// </summary>
public class OP01_050_Penguin : IScriptedEffect
{
    public string CardNumber => "OP01-050";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 我方场上已存在"夏齐"则不发动
        if (me.Characters.Any(c => c.MatchesName("夏齐"))) return;

        var candidates = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.MatchesName("夏齐")).ToList();
        if (candidates.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多 1 张\"夏齐\"登场",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = candidates.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
