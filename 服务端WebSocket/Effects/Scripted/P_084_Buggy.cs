using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-084 巴奇（角色 / 水 / 四皇·十字公会）
/// 此角色无法攻击。（由引擎依 Abilities 自动处理。）
/// 我方领袖为"巴奇"的场合，所有费用为 3 和 4 的角色无法攻击。
///   —— 用 ContinuousEffect.GrantRestriction=CannotAttack 注册（OnEnterField）：谓词在领袖名为
///      "巴奇"时，对原本费用为 3/4 的卡（双方"所有角色"）生效；HasContinuousRestriction 不校验
///      Scope，故谓词内显式按 Info.Cost 判定。来源(本卡)离场时引擎自动清理。
/// 【登场时】将我方手牌中最多 1 张费用不高于 6 且拥有《十字公会》特征的角色卡牌登场。
/// </summary>
public class P_084_Buggy : IScriptedEffect
{
    public string CardNumber => "P-084";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;

        // 持续：领袖为"巴奇"时，所有原本费用为 3/4 的角色无法攻击
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = -1, IncludeLeader = false, IncludeCharacters = true },
            GrantRestriction = RestrictionKind.CannotAttack,
            Predicate = (s, sideIdx, card) =>
                s.Players[owner].Leader.Info.NameContains("巴奇") &&
                (card.Info.Cost == 3 || card.Info.Cost == 4),
        });

        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 6 &&
            c.Info.HasKeyword("十字公会")).ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场 1 张费用≤6 的《十字公会》角色（最多 1 张）",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;
        var picked = playable.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
    }
}
