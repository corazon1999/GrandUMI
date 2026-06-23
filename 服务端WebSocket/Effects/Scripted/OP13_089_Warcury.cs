using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-089 托普曼·沃丘利圣（4 费 5000，天龙人/五老星）
/// 原文：
///   我方废弃区中有 7 张或更多卡牌的场合，此角色不会因对方的效果而离场，并获得【阻挡者】效果。
///   【KO时】抽取 1 张卡牌。
///
/// 本脚本实现：
///   (1) 登场时注册 ContinuousEffect：我方废弃区≥7 时此角色获得【阻挡者】，且不会因效果离场
///       （LeaveGuard="effect"；引擎粒度为"任意效果"，略宽于卡面"对方的效果"，含 KO/回手/回卡组等）。
///   (2)【KO时】抽取 1 张卡牌。
/// </summary>
public class OP13_089_Warcury : IScriptedEffect
{
    public string CardNumber => "OP13-089";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnKO || t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;

        // ── ① 持续光环：我方废弃区≥7 → 此角色获得【阻挡者】，且不会因效果离场（登场时注册）──
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var selfId0 = ctx.Source.Id;
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId0.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId0.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "阻挡者",
                LeaveGuard = "effect",   // 引擎粒度为"任意效果离场"，略宽于卡面"对方的效果"
                Predicate = (st, side, card) => card.Id == selfId0 && st.Players[owner].Trash.Count >= 7,
            });
            return;
        }

        // ── ②【KO时】抽取 1 张卡牌 ──
        AtomicOps.Draw(ctx.State, owner, 1);
        await Task.CompletedTask;
    }
}
