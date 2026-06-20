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
/// 本脚本实现 (2)：【KO时】从我方废弃区中选最多 1 张事件加入手牌（可选，min=0）。
/// 候选来自废弃区，经 extra.choiceCards 下发卡面。
///
/// 简化点：(1) 条件式获得【阻挡者】未实现 —— 引擎 HasKeyword 仅做卡面文本
///   contains("【阻挡者】") + 临时关键字判定，无法按"废弃区事件≥4"这一游戏状态条件
///   动态评估；ContinuousEffect 也仅支持力量修正，无条件式关键字钩子。
///   （注意：由于卡面文本含"【阻挡者】"字样，现引擎会将此角色恒视为带【阻挡者】，
///    无法区分条件是否满足。此为既有引擎缺口，非本脚本可修正。）
/// </summary>
public class OP12_065_Ivankov : IScriptedEffect
{
    public string CardNumber => "OP12-065";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

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
