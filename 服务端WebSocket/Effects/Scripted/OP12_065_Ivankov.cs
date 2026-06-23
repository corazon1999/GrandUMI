using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-065 安普里奥·伊万科夫（6 费 7000，因佩尔地狱/革命军）
///
/// 卡面效果：
///   1. 我方废弃区中有 4 张或更多事件的场合，此角色获得【阻挡者】效果。（条件式静态关键字）
///   2. 【KO时】将我方废弃区中最多 1 张事件加入手牌。
///
/// 本脚本实现：
///   (1) 登场时注册 ContinuousEffect：我方废弃区事件≥4 时此角色获得【阻挡者】（按废弃区动态评估）。
///   (2)【KO时】从我方废弃区中选最多 1 张事件加入手牌（可选，min=0）。候选经 extra.choiceCards 下发卡面。
/// </summary>
public class OP12_065_Ivankov : IScriptedEffect
{
    public string CardNumber => "OP12-065";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnKO || t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;

        // ── ① 持续光环：我方废弃区事件≥4 → 此角色获得【阻挡者】（登场时注册，按废弃区动态评估）──
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var selfId0 = ctx.Source.Id;
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId0.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId0.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "阻挡者",
                Predicate = (st, side, card) => card.Id == selfId0 &&
                    st.Players[owner].Trash.Count(c => c.Info.Kind == CardKind.Event) >= 4,
            });
            return;
        }

        // ── ②【KO时】将我方废弃区中最多 1 张事件加入手牌 ──
        var me = ctx.State.Players[owner];

        // 候选：我方废弃区中的事件卡
        var candidates = me.Trash.Where(c => c.Info.Kind == CardKind.Event).ToList();
        if (candidates.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrashEvent",
            "将我方废弃区中最多 1 张事件加入手牌",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (picked is null) return;

        AtomicOps.TrashToHand(me, picked);
    }
}
