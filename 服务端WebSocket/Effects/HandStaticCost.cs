using GrandUMI.Game;

namespace GrandUMI.Effects;

/// <summary>
/// 手牌中的「静态费用修正」通道。
///
/// 背景：少数卡的效果是「（满足某场况时）手牌中的此卡牌费用-N」。这类被动只在卡处于手牌时生效，
/// 与 <see cref="ContinuousEffect"/>（仅对场上卡评估、需登场时注册）不在同一通道，过去整体未实现。
/// 本类提供统一入口：给定一张手牌卡 + 当前局面，返回它当前应享受的费用增量（≤0 表示减费）。
///
/// 调用点：
///   - <see cref="GameState.HandPlayCost"/>：决定实际打出扣费；
///   - 快照 StateSnapshotBuilder：下发每张手牌的有效费用供客户端显示。
///
/// 力量判定走 <see cref="GameState.CurrentPowerOf"/>（含咚加成/持续修正），特征走 <see cref="Cards.CardInfo.HasKeyword"/>。
/// </summary>
public static class HandStaticCost
{
    /// <summary>返回该手牌卡当前的静态费用增量（满足条件时为负，否则 0）。card 须为 playerIdx 一方手牌中的卡。</summary>
    public static int Delta(GameState state, int playerIdx, CardInstance card)
    {
        var me = state.Players[playerIdx];

        switch (card.Info.Number)
        {
            // OP16-005 沙奇：我方场上存在力量≥8000 且特征含〈白胡子海盗团〉的角色 → 费用-3
            case "OP16-005":
                return me.Characters.Any(c =>
                    state.CurrentPowerOf(playerIdx, c) >= 8000 && c.Info.HasKeyword("白胡子海盗团"))
                    ? -3 : 0;

            // OP15-102 甘·福尔：我方场上存在力量≥7000 且特征含〈空岛〉的角色 → 费用-3
            case "OP15-102":
                return me.Characters.Any(c =>
                    state.CurrentPowerOf(playerIdx, c) >= 7000 && c.Info.HasKeyword("空岛"))
                    ? -3 : 0;

            // OP16-015 蒙奇·D·路飞：我方领袖名称含〈艾斯〉且我方场上咚!!≥6 张 → 费用-2
            case "OP16-015":
                return (me.Leader.Info.NameContains("艾斯") && me.TotalDonInCostArea >= 6)
                    ? -2 : 0;

            // OP11-023 阿龙：我方领袖含〈鱼人族〉特征、我方生命≤3、对方场上休息状态卡≥5 → 费用-3
            case "OP11-023":
            {
                var opp = state.Players[1 - playerIdx];
                int oppTapped = opp.Characters.Count(c => c.IsTapped)
                              + (opp.StageCard?.IsTapped == true ? 1 : 0)
                              + (opp.Leader.IsTapped ? 1 : 0);
                return (me.Leader.Info.HasKeyword("鱼人族") && me.LifeCount <= 3 && oppTapped >= 5)
                    ? -3 : 0;
            }

            // EB04-061 蒙奇·D·路飞：我方生命卡牌不多于 1 张 → 费用-1
            case "EB04-061":
                return me.LifeCount <= 1 ? -1 : 0;

            // ST23-001 乌塔：我方场上存在力量≥10000 的角色 → 费用-4
            case "ST23-001":
                return me.Characters.Any(c => state.CurrentPowerOf(playerIdx, c) >= 10000) ? -4 : 0;

            // ST23-002 杰克斯：对方场上存在原本力量≥8000 的角色 → 费用-3
            case "ST23-002":
                return state.Players[1 - playerIdx].Characters.Any(c => c.Info.Power >= 8000) ? -3 : 0;

            // ST26-001 面条假面：我方场上存在原本力量≥7000 的“山五郎”或“山智” → 费用-5
            case "ST26-001":
                return me.Characters.Any(c => c.Info.Power >= 7000 &&
                    (c.Info.NameIs("山五郎") || c.Info.NameIs("山智"))) ? -5 : 0;

            // PRB02-014 萨波：我方废弃区中有 15 张或更多卡牌 → 费用-3
            case "PRB02-014":
                return me.Trash.Count >= 15 ? -3 : 0;

            // OP15-013 剪刀：我方领袖力量不高于 0 → 费用-2
            case "OP15-013":
                return state.CurrentPowerOf(playerIdx, me.Leader) <= 0 ? -2 : 0;

            // OP07-064 山智：我方场上咚!!张数比对方场上咚!!张数少 2 张或更多 → 费用-3
            case "OP07-064":
                return me.TotalDonInCostArea + 2 <= state.Players[1 - playerIdx].TotalDonInCostArea
                    ? -3 : 0;

            default:
                return 0;
        }
    }
}
