using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-003 糖糖（领航 / 炎暗 4 费 5000，堂吉诃德海盗团）
/// 1.【我方的回合结束时】我方场上存在力量为6000或更高且拥有《堂吉诃德海盗团》特征的角色的场合，
///    将我方最多1张咚!!转为活跃状态。
/// 2.【对方的回合中】【每回合1次】当我方发动事件时，从咚!!卡组中追加最多1张活跃状态的咚!!。
///
/// 实现说明：
/// - 第一段：OnMyTurnEnd 时机，检查我方是否存在「当前力量≥6000 且含《堂吉诃德海盗团》」的角色，
///   满足则把最多 1 张休息状态的咚!!转为活跃。
/// - 第二段监听统一事件发动钩子 OnOppEventPlayed，并按 payload.owner 判定实际发动方；
///   仅在对方回合、每回合首次由本方发动事件时可从咚卡组追加 1 张活跃咚。
/// </summary>
public class OP10_003_Sugar : IScriptedEffect
{
    public string CardNumber => "OP10-003";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnMyTurnEnd || t == EffectTrigger.OnOppEventPlayed;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnOppEventPlayed)
        {
            int eventOwner = ctx.Vars.TryGetValue("owner", out var raw) && raw is int value ? value : -1;
            string key = $"OP10-003-event:{ctx.Source.Id}";
            if (eventOwner != owner
                || ctx.State.CurrentTurnPlayer == owner
                || me.TurnOnceUsed.Contains(key)
                || me.DonDeck.Count == 0
                || me.CostArea.Count >= 10)
                return;

            if (!await ctx.Prompts.ConfirmOptional(owner, "糖糖：从咚!!卡组追加最多1张活跃咚!!？"))
                return;
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
            me.TurnOnceUsed.Add(key);
            return;
        }

        // 条件：我方场上存在力量≥6000且含《堂吉诃德海盗团》特征的角色
        bool ok = me.Characters.Any(c =>
            c.Info.HasKeyword("堂吉诃德海盗团") &&
            ctx.State.CurrentPowerOf(owner, c) >= 6000);
        if (!ok) return;

        // 将我方最多 1 张休息状态的咚!!转为活跃状态
        foreach (var d in me.CostArea)
        {
            if (d.State == DonState.Rest)
            {
                d.State = DonState.Active;
                d.AttachedToCardId = null;
                break;
            }
        }
    }
}
