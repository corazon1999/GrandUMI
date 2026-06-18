using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-109 哈古瓦尔·D·萨乌罗（角色 / 光 3 费 5000，奥哈拉/巨人族/海军）
///
/// 完整文本：
///   【阻挡者】（对方攻击后，可以将此卡牌转为休息状态，并将攻击对象改为此卡牌）
///   【触发】我方领袖为"妮古·罗宾"的场合，此卡牌登场。
///
/// 实现：
///   - 【阻挡者】为纯关键词，由引擎处理，脚本不实现。
///   - 生命牌【触发】(OnLifeRevealTrigger)：触发发动时本卡已被放入废弃区。
///     条件为我方领袖名为"妮古·罗宾"时，从废弃区免费以活跃状态登场（"此卡牌登场"）。
///
/// 简化点：trigger 节 DSL 不支持 if 条件，故用脚本判定领袖名称。
/// </summary>
public class OP09_109_JaguarDSaul : IScriptedEffect
{
    public string CardNumber => "OP09-109";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        // 条件：我方领袖为"妮古·罗宾"
        if (!me.Leader.Info.NameContains("妮古·罗宾")) return Task.CompletedTask;

        // 此卡牌登场：触发发动时本卡在废弃区，从废弃区免费以活跃状态登场
        if (!me.Trash.Contains(ctx.Source)) return Task.CompletedTask;
        AtomicOps.PlayFromTrashFree(s, ctx.OwnerIndex, ctx.Source, restState: false);

        return Task.CompletedTask;
    }
}
