using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-060 战国（领航）
/// 启动主要：将 8 张活跃咚放回咚卡组 → 从手牌选最多 3 张卡名不同的《大将》角色登场
/// </summary>
public class OP16_060_Sengoku : IScriptedEffect
{
    public string CardNumber => "OP16-060";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.ActiveDonCount < 8) return;

        // 支付：把 8 张咚放回咚卡组
        AtomicOps.ReturnDonToDeck(me, 8);

        // 从手牌选最多 3 张卡名不同的《大将》角色登场
        var candidates = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character && c.Info.HasKeyword("大将"))
            .ToList();
        if (candidates.Count == 0) return;

        var pickedNames = new HashSet<string>();
        for (int i = 0; i < 3; i++)
        {
            var pool = candidates.Where(c => !pickedNames.Contains(c.Info.Name) && me.Hand.Contains(c)).ToList();
            if (pool.Count == 0) break;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandShogun",
                $"选 1 张卡名不同的《大将》角色登场（{i + 1}/3）",
                pool.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count == 0) break;
            var card = pool.First(c => c.Id.ToString() == chosen[0]);
            pickedNames.Add(card.Info.Name);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, card);
        }
    }
}
