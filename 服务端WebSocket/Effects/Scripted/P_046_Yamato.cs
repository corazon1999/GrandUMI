using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-046 大和（角色）
/// 【登场时】可以将我方所有手牌自选顺序放回卡组最下方。那样做的场合，抽取放回的张数的卡牌。
///
/// 实现说明：
/// - "自选顺序放回卡组最下方"简化为按原相对顺序放底（对实战影响极小）。
/// - 放回张数 = 发动时除本卡外的全部手牌张数；之后抽取相同张数。
/// </summary>
public class P_046_Yamato : IScriptedEffect
{
    public string CardNumber => "P-046";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        var me = ctx.State.Players[owner];

        // 当前手牌（本卡此时已登场，不在手牌中）
        var hand = me.Hand.ToList();
        if (hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(owner,
            "大和【登场时】：将我方所有手牌放回卡组最下方，并抽取相同张数？");
        if (!use) return;

        int n = hand.Count;
        foreach (var c in hand)
            AtomicOps.ReturnHandToDeckBottom(me, c);

        AtomicOps.Draw(ctx.State, owner, n);
    }
}
