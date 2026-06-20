using System.Linq;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-017 小奥兹Jr.（角色，费用4 / 力量8000 / 巨人族・白胡子海盗团旗下）
/// 持续：我方场上不存在「费用为8或更高且特征中包含〈白胡子海盗团〉」的角色的场合，此角色力量 -4000。
/// 【阻挡者】（被动关键字，由 keyWords 处理，不在本脚本内）
///
/// 实现说明：
///   - 登场时注册一条 ContinuousEffect，PowerDelta = -4000，仅作用于自身（Predicate 限定 c.Id == selfId）。
///   - Predicate 为动态实时判断：场上满足条件的角色登场/离场时，减力会自动失效/重新生效
///     （战斗 BattleEngine 与快照 StateSnapshotBuilder 均走 GameState.CurrentPowerOf → ContinuousPowerBonus）。
///   - 费用判定走 GameState.CurrentCostOf（兼容持续费用光环），特征用精确匹配的 HasKeyword
///     （故自身的「白胡子海盗团旗下」不会被误判为「白胡子海盗团」）。
///   - 来源卡离场时由 TurnEngine 按 SourceCardId 自动清理。
/// </summary>
public class OP16_017_LittleOzJr : IScriptedEffect
{
    public string CardNumber => "OP16-017";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;

        // 防重复注册（同名卡多次登场 / 再入）
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 减力激活条件：我方场上「不存在」费用≥8 且特征含〈白胡子海盗团〉的角色
        bool Weaken(GameState s, int sideIdx, CardInstance c)
        {
            if (sideIdx != owner || c.Id != selfId) return false;   // 仅作用于自身
            var me = s.Players[owner];
            bool hasBigWhitebeard = me.Characters.Any(ch =>
                s.CurrentCostOf(owner, ch) >= 8 && ch.Info.HasKeyword("白胡子海盗团"));
            return !hasBigWhitebeard;                               // 不存在 → 减力 -4000
        }

        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = -4000,
            Predicate = Weaken,
        });

        return Task.CompletedTask;
    }
}
