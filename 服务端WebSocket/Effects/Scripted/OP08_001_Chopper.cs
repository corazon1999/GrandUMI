using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-001 托尼托尼·乔巴（领袖）
/// 【启动主要】【每回合1次】赋予我方最多 3 张拥有《动物》或《铁桶王国》特征的角色各最多 1 张休息状态的咚!!。
///
/// 说明 / 简化点：
///   - "最多 3 张不同角色各贴 1 张休息咚"用循环逐张选择实现：每轮从剩余符合特征且尚未选中的
///     角色里选 1 张并贴 1 张休息咚，最多 3 轮；玩家可随时选 0 张提前结束。
///   - 每张角色仅获得 1 张咚（已贴过的从候选中剔除）。受可用咚数量限制（AttachDonFromCost 内部处理）。
/// </summary>
public class OP08_001_Chopper : IScriptedEffect
{
    public string CardNumber => "OP08-001";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        bool Eligible(CardInstance c) =>
            c.Info.HasKeyword("动物") || c.Info.HasKeyword("铁桶王国");

        var picked = new HashSet<string>();
        for (int i = 0; i < 3; i++)
        {
            var cands = me.Characters.Where(c => Eligible(c) && !picked.Contains(c.Id.ToString())).ToList();
            if (cands.Count == 0) break;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                $"选择 1 张拥有《动物》或《铁桶王国》特征的角色，赋予 1 张休息咚（剩余 {3 - i} 次，可选 0 张结束）",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count == 0) break;

            var target = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Rest);
            picked.Add(target.Id.ToString());
        }

        me.TurnOnceUsed.Add(key);
    }
}
