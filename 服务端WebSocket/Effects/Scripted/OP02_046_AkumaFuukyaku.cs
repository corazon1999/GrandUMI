using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-046 恶魔风脚 野兽肉踢（事件 / 风 cost2）
/// 【主要】将对方最多 1 张休息状态的费用不高于 4 的角色 KO。
/// 【触发】将我方手牌中最多 1 张费用不高于 4 且原本没有效果的角色卡牌登场。
///
/// 实现说明：
///   - 【主要】(EventMain)：候选为对方休息状态且费用≤4 的角色，选 0..1 KO。
///   - 【触发】(OnLifeRevealTrigger)：登场最多 1 张 cost≤4 且「原本没有效果」的角色。
///   - 「原本没有效果」判定：无 EffectTags、无 Abilities、无 Trigger 文本。
/// </summary>
public class OP02_046_AkumaFuukyaku : IScriptedEffect
{
    public string CardNumber => "OP02-046";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    private static bool HasNoOriginalEffect(CardInstance c) =>
        c.Info.EffectTags.Length == 0 && c.Info.Abilities.Length == 0 && string.IsNullOrEmpty(c.Info.Trigger);

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            var playable = me.Hand.Where(c =>
                c.Info.Kind == CardKind.Character && c.Info.Cost <= 4 && HasNoOriginalEffect(c)).ToList();
            if (playable.Count == 0) return;

            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
                "登场最多 1 张 费用≤4 且原本没有效果的角色",
                playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = playable.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.PlayFromHandFree(ctx.State, owner, picked);
            }
            return;
        }

        // 【主要】将对方最多 1 张休息状态、费用≤4 的角色 KO
        var cands = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 4).ToList();
        if (cands.Count == 0) return;
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张休息状态、费用≤4 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
