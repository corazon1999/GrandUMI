using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-033 莫里（角色 / 风 / 巨人族・革命军，cost4 power5000）
/// 此角色将要被KO的场合，可以改为将我方 2 张卡牌转为休息状态，使此角色不会被KO。
/// 【不可阻挡】（关键词，引擎处理）
///
/// 实现说明（带代偿成本的被动替换型防KO，走 PreKO）：
///   - PreKO 触发时本卡为将被KO的受害者(ctx.Source)。可选支付成本：将我方 2 张活跃卡牌
///     （领袖/角色）转为休息状态，支付后 MarkPreventKO 取消本次KO。
///   - 成本不足（活跃卡<2）时无法支付，不发动。
/// </summary>
public class OP16_033_Mole : IScriptedEffect
{
    public string CardNumber => "OP16-033";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.PreKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 可支付成本的活跃卡牌（领袖 + 角色）
        var costCands = new List<CardInstance>();
        if (!me.Leader.IsTapped) costCands.Add(me.Leader);
        costCands.AddRange(me.Characters.Where(c => !c.IsTapped));
        if (costCands.Count < 2) return; // 无法支付 2 张休息成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "莫里：将我方 2 张卡牌转为休息状态，使此角色不会被KO？");
        if (!use) return;

        var costPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "将我方 2 张卡牌转为休息状态（成本）",
            costCands.Select(c => c.Id.ToString()).ToList(), 2, 2);
        if (costPick.Count < 2) return; // 成本未支付完整

        foreach (var cid in costPick)
        {
            var c = costCands.First(x => x.Id.ToString() == cid);
            AtomicOps.RestCard(c);
        }
        ctx.State.MarkPreventKO(self.Id);
    }
}
