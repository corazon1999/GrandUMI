using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-003 爱德华·纽哥特（角色）
/// 持续：【我方的回合中】我方领袖获得【双重攻击】，力量 +2000。
/// 【登场时】可以公开我方手牌中 2 张力量为 8000 的角色卡牌：本回合中，对方最多 1 张角色力量 -6000。
///
/// 实现说明：
///   - 持续部分为静态效果，登场时注册 ContinuousEffect，离场时由 TurnEngine 自动清理。
///     【双重攻击】用 GrantKeyword，力量 +2000 用 PowerDelta；谓词限定"我方领袖 + 我方回合"。
///     两条持续查询路径（ContinuousPowerBonus / HasContinuousKeyword）均只看 Predicate，
///     故 Scope 仅作语义标注，实际作用域完全由 Predicate 编码。
///   - 【登场时】为"可以…：…"可选效果，含公开成本：需手动选 2 张力量 8000 的角色公开，
///     才会触发对方角色 -6000。整段在脚本内实现（不再委托 DSL），OP16.json 中该卡登场定义因
///     脚本覆盖而失效（保留作文档）。
/// </summary>
public class OP16_003_Newgate : IScriptedEffect
{
    public string CardNumber => "OP16-003";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        // 防重复注册（同名卡多次登场 / 再入）
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 谓词：受影响卡为"我方领袖"且当前为"我方回合"
        bool BuffsLeader(GameState s, int sideIdx, CardInstance c) =>
            sideIdx == owner
            && c.Id == s.Players[owner].Leader.Id
            && s.CurrentTurnPlayer == owner;

        // 持续①：【我方的回合中】我方领袖获得【双重攻击】
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            GrantKeyword = "双重攻击",
            Predicate = BuffsLeader,
        });

        // 持续②：【我方的回合中】我方领袖力量 +2000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            PowerDelta = 2000,
            Predicate = BuffsLeader,
        });

        // ── 【登场时】可以公开手牌 2 张力量 8000 的角色：本回合对方最多 1 张角色 -6000 ──
        var me = ctx.State.Players[owner];

        // 成本候选：手牌中力量为 8000 的角色卡牌
        var revealCands = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character && c.Info.Power == 8000)
            .ToList();
        // 公开成本固定为 2 张，凑不够则无法支付 → 效果不发动
        if (revealCands.Count < 2) return;

        // "可以…" 为可选效果
        bool use = await ctx.Prompts.ConfirmOptional(owner,
            "纽哥特：公开手牌中 2 张力量 8000 的角色，使对方最多 1 张角色本回合力量 -6000？");
        if (!use) return;

        // 手动选 2 张公开（手牌为私有区，需 choiceCards 下发番号才能在客户端显示牌面）
        var revealExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = revealCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var revealChosen = await ctx.Prompts.ChooseCards(owner, "RevealOwnHand",
            "选择 2 张力量 8000 的角色公开（作为发动成本）",
            revealCands.Select(c => c.Id.ToString()).ToList(), 2, 2, revealExtra);
        if (revealChosen.Count < 2) return;   // 放弃支付

        // 向对手公开演出
        var revealedNumbers = revealChosen
            .Select(id => revealCands.First(c => c.Id.ToString() == id).Info.Number)
            .ToList();
        ctx.Engine?.BroadcastReveal(owner, revealedNumbers);

        // 选对方最多 1 张角色，本回合力量 -6000
        var opp = ctx.State.Players[1 - owner];
        var oppCands = opp.Characters.ToList();
        if (oppCands.Count == 0) return;

        var tgtExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = oppCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var tgtChosen = await ctx.Prompts.ChooseCards(owner, "OpponentCharacter",
            "选择对方最多 1 张角色，本回合力量 -6000",
            oppCands.Select(c => c.Id.ToString()).ToList(), 0, 1, tgtExtra);
        if (tgtChosen.Count == 0) return;

        var tgt = oppCands.First(c => c.Id.ToString() == tgtChosen[0]);
        AtomicOps.AddPowerThisTurn(tgt, -6000);
    }
}
