using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-026 山智（领航 / 风·水 4 费 4000）
/// 【每回合1次】将我方手牌中原本没有效果的角色登场时，我方场上的角色不多于3张的场合，
///   将我方最多2张咚!!转为活跃状态。
///
/// 实现说明：
///   - "原本没有效果的角色登场时" → OnAllyCharEnter（我方他角色登场时），按 cardId 找到登场卡，
///     判其 Info.EffectText 为空（原本没有效果）。
///   - "我方场上的角色不多于3张"：在登场卡已加入 Characters 后判定 Count <= 3。
///   - "最多2张咚!!转为活跃"：将我方休息状态的咚转为活跃，最多2张。
/// </summary>
public class OP02_026_Sanji : IScriptedEffect
{
    public string CardNumber => "OP02-026";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyCharEnter;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 仅我方角色登场触发
        var owner = ctx.Vars.TryGetValue("owner", out var ov) ? (ov is int oi ? oi : -1) : -1;
        if (owner != ctx.OwnerIndex) return Task.CompletedTask;

        var enteredId = ctx.Vars.TryGetValue("cardId", out var cv) ? cv as string : null;
        if (string.IsNullOrEmpty(enteredId)) return Task.CompletedTask;

        // 登场卡须为"原本没有效果的角色"（无效果标签/能力关键词/触发），且不是自身
        var entered = me.Characters.FirstOrDefault(c => c.Id.ToString() == enteredId);
        if (entered is null || entered.Id == self.Id) return Task.CompletedTask;
        if (entered.Info.EffectTags.Length > 0 || entered.Info.Abilities.Length > 0
            || !string.IsNullOrWhiteSpace(entered.Info.Trigger)) return Task.CompletedTask;

        // 我方场上的角色不多于3张
        if (me.Characters.Count > 3) return Task.CompletedTask;

        // 每回合1次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        // 将我方最多2张休息状态的咚!!转为活跃状态
        int n = 0;
        foreach (var d in me.CostArea)
        {
            if (n >= 2) break;
            if (d.State == DonState.Rest) { d.State = DonState.Active; n++; }
        }

        return Task.CompletedTask;
    }
}
