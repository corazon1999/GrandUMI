using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-024 哈姆雷特（角色 / 水 / 百兽海盗团·SMILE）
/// 持续：我方手牌不多于 4 张的场合，我方所有拥有《SMILE》特征的角色力量 +1000。
///
/// 实现说明：
///   - 纯持续/静态力量修正，用 ContinuousEffect 注册（OnEnterField 时机）。
///   - Scope 限我方角色，Filter 限《SMILE》特征；Predicate 判我方手牌张数 ≤ 4。
///   - 来源卡离场时引擎自动清理；重复登场前先 RemoveAll 去重。
/// </summary>
public class EB01_024_Hamlet : IScriptedEffect
{
    public string CardNumber => "EB01-024";

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
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = c => c.Info.HasKeyword("SMILE"),
            },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) => s.Players[owner].Hand.Count <= 4,
        });

        return Task.CompletedTask;
    }
}
