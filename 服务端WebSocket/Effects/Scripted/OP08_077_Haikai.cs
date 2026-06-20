using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-077 霸海（6 费 事件）
/// 【主要】咚!!-2（可以将我方场上指定数量的咚‼放回咚‼卡组）：
///   我方领袖拥有《百兽海盗团》或《大妈海盗团》特征的场合，将对方最多 2 张费用不高于 6 的角色 KO。
///
/// 实现说明：
/// - 领袖条件为 OR（《百兽海盗团》或《大妈海盗团》），DSL 的 if 多键仅支持 AND，
///   故用脚本以 || 直接表达。条件不满足时不发动效果。
/// - "咚!!-2" 为本效果成本：将我方场上 2 张咚放回咚卡组（ReturnDonToDeck）。
///   场上可用咚不足 2 张时无法支付成本，效果不发动。
/// - KO 对方最多 2 张费用不高于 6 的角色（min=0，玩家可少选）。
/// </summary>
public class OP08_077_Haikai : IScriptedEffect
{
    public string CardNumber => "OP08-077";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 领袖条件（OR）：拥有《百兽海盗团》或《大妈海盗团》
        bool leaderOk = me.Leader.Info.HasKeyword("百兽海盗团") || me.Leader.Info.HasKeyword("大妈海盗团");
        if (!leaderOk) return;

        // 成本：咚!!-2（将我方场上 2 张咚放回咚卡组）。可用咚不足则无法支付。
        if (me.CostArea.Count < 2) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 2)) return;

        // 效果：KO 对方最多 2 张费用不高于 6 的角色
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 6).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 2 张费用不高于 6 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 2, extra);

        foreach (var id in chosen)
        {
            var tgt = cands.FirstOrDefault(c => c.Id.ToString() == id);
            if (tgt is not null)
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
