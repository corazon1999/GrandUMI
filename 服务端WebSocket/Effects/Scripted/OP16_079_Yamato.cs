using System.Linq;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-079 大和（领航）
/// 当将我方废弃区中拥有《和之国》特征的角色登场时，本回合中，该角色获得【速攻】。
///
/// 实现：监听 OnAllyCharEnter（批次1 起，PlayFromTrashFree 登场也会派发，payload 含 from="trash"）。
///   仅当登场方为我方、来源 from=="trash"、登场卡含《和之国》时，赋予其本回合【速攻】。
///   （从废弃区登场的角色 TurnPlayed=本回合，原本不能攻击；获得速攻后可在登场回合攻击。）
/// </summary>
public class OP16_079_Yamato : IScriptedEffect
{
    public string CardNumber => "OP16-079";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyCharEnter;

    public Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;

        // 登场方须为我方
        int enterOwner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (enterOwner != owner) return Task.CompletedTask;

        // 仅"从废弃区登场"
        var from = ctx.Vars.TryGetValue("from", out var fv) ? fv as string : null;
        if (from != "trash") return Task.CompletedTask;

        // 登场卡含《和之国》
        var cardId = ctx.Vars.TryGetValue("cardId", out var cv) ? cv as string : null;
        var me = ctx.State.Players[owner];
        var entered = cardId is not null ? me.Characters.FirstOrDefault(c => c.Id.ToString() == cardId) : null;
        if (entered is null || !entered.Info.HasKeyword("和之国")) return Task.CompletedTask;

        AtomicOps.GiveKeyword(entered, "速攻", KeywordDuration.ThisTurn);
        return Task.CompletedTask;
    }
}
