using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// PRB02-001 可比（角色 / 炎 / 海军・利刃 / cost4 power5000 counter1000）
/// ①【对方的回合中】我方领袖拥有《海军》特征的场合，此角色的力量+1000。
/// ②【攻击时】将对方最多1张原本的力量不高于3000的角色KO。之后，我方手牌不多于6张的场合，抽取1张卡牌。
///
/// 实现说明：
///   - ① 为条件持续光环：登场时注册 ContinuousEffect（仅作用自身，Predicate = 对方回合中 且 我方领袖含《海军》，PowerDelta=+1000）。
///   - ② 为【攻击时】触发：KO 对方原本力量(印刷力量)≤3000 的角色，之后我方手牌≤6 则抽 1（迁移自原 DSL，脚本统一实现）。
///   - 本卡同时有持续光环+触发效果，故整卡用 C# 脚本实现（脚本优先于 DSL，原 PRB02.json 条目已移除）。
/// </summary>
public class PRB02_001_Koby : IScriptedEffect
{
    public string CardNumber => "PRB02-001";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;
        var me = ctx.State.Players[owner];
        var opp = ctx.State.Players[1 - owner];

        // ── ①【对方的回合中】我方领袖含《海军》→ 此角色 +1000（持续光环，登场时注册）──
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true, Filter = c => c.Id == selfId },
                PowerDelta = 1000,
                // 注：引擎 ContinuousPowerBonus 仅按 Predicate 判定、不应用 Scope，故须在此显式限定仅作用本卡自身
                Predicate = (s, side, card) =>
                    card.Id == selfId && s.CurrentTurnPlayer != owner && s.Players[owner].Leader.Info.HasKeyword("海军"),
            });
            return;
        }

        // ── ②【攻击时】KO 对方最多1张原本力量≤3000 的角色；之后我方手牌≤6 抽1 ──
        var cands = opp.Characters.Where(c => c.Info.Power <= 3000).ToList();
        if (cands.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var pick = await ctx.Prompts.ChooseCards(owner, "OpponentCharacter",
                "将对方最多1张原本力量≤3000的角色KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (pick.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == pick[0]);
                await AtomicOps.KOByEffectAsync(ctx.State, 1 - owner, tgt, ctx.Prompts, owner);
            }
        }

        // 之后：我方手牌不多于6张 → 抽1
        if (me.Hand.Count <= 6)
            AtomicOps.Draw(ctx.State, owner, 1);
    }
}
