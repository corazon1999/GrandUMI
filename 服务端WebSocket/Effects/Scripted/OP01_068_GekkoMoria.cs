using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-068 月光·莫利亚（角色 / 水 / 王下七武海·恐怖之船海盗团 / 4 费 5000）
/// 【我方的回合中】我方持有 5 张或更多手牌的场合，此角色获得【双重攻击】效果。
///
/// 实现说明：
///   - 条件性持续关键词授予（Wave1 通道）：注册 ContinuousEffect.GrantKeyword="双重攻击"，
///     Predicate 选中本卡自身，且仅在"我方回合 + 我方手牌≥5"时成立。
///   - 在 OnEnterField 注册；本卡离场引擎自动清理。先 RemoveAll 同源防重复。
/// </summary>
public class OP01_068_GekkoMoria : IScriptedEffect
{
    public string CardNumber => "OP01-068";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "双重攻击",
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner && c.Id == selfId &&
                s.CurrentTurnPlayer == owner &&
                s.Players[owner].Hand.Count >= 5,
        });

        return Task.CompletedTask;
    }
}
