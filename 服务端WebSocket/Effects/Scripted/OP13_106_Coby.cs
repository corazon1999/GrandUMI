using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-106 柯妮（角色 / 光 1 费 1000，邦妮海盗团）
///
/// 完整文本：
///   【触发】此卡牌登场。
///   【对方的回合中】当发动【触发】效果时，本回合中，此角色获得【阻挡者】效果。
///
/// 实现：
///   - 本卡只有生命牌【触发】这一处效果。生命牌触发发动时，LifeRevealManager 已把本卡放入废弃区，
///     这里把它从废弃区免费登场（以活跃状态登场，符合“此卡牌登场”）。
///   - “【对方的回合中】当发动【触发】效果时，本回合获得【阻挡者】”：
///     生命牌触发只会在对方攻击（即对方回合）时发动，故触发发动时一定处于对方回合，
///     登场后给本卡授予本回合【阻挡者】。为稳妥仍判定 CurrentTurnPlayer != 自己 再授予。
///
/// 简化点：无（“此卡牌登场”+对方回合获得阻挡者均已实现）。
/// </summary>
public class OP13_106_Coby : IScriptedEffect
{
    public string CardNumber => "OP13-106";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        // 触发发动时本卡已被放入废弃区，从废弃区免费以活跃状态登场（“此卡牌登场”）
        if (!me.Trash.Contains(ctx.Source)) return Task.CompletedTask;
        AtomicOps.PlayFromTrashFree(s, ctx.OwnerIndex, ctx.Source, restState: false);

        // 仅当确实登场到场上才继续（场上满 5 张时本卡可能未能登场——PlayFromTrashFree 会先牺牲最左侧角色，
        // 故本卡通常能登场；若仍不在场上则不授予）
        if (!me.Characters.Contains(ctx.Source)) return Task.CompletedTask;

        // 【对方的回合中】当发动【触发】效果时：本回合获得【阻挡者】
        // 生命牌触发只在对方回合发动，这里再判定一次以保证语义。
        if (s.CurrentTurnPlayer != ctx.OwnerIndex)
        {
            AtomicOps.GiveKeyword(ctx.Source, "阻挡者", KeywordDuration.ThisTurn);
        }

        return Task.CompletedTask;
    }
}
