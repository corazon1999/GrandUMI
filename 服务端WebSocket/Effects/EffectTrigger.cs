namespace GrandUMI.Effects;

/// <summary>
/// 效果触发时机
/// </summary>
public enum EffectTrigger
{
    OnEnterField,           // 【登场时】
    OnAttackDeclare,        // 【攻击时】
    OnOppAttackDeclare,     // 【对方的攻击时】
    OnBlockDeclare,         // 【阻挡时】
    PreKO,                  // KO 前置换钩子（让"改为...不被 KO"效果有机会取消本次 KO）
    OnKO,                   // 【K.O.时】（在卡进入废弃区后）
    OnDamageToLeader,       // 给对方领袖造成伤害时
    OnLifeRevealTrigger,    // 生命牌触发
    OnGameStart,            // 对局开始时（双方完成 mulligan、首回合开始后触发一次，用于注册领袖等永续被动）
    OnTurnStart,            // 回合开始时
    OnMyTurnEnd,            // 【我方的回合结束时】
    OnOppTurnEnd,           // 【对方的回合结束时】
    OnDonAttached,          // 咚被赋予时
    OnDrawCard,             // 抽牌时
    OnPlayCard,             // 出牌时（与 OnEnterField 不同：事件也算 play）
    OnEnterTrash,           // 进入废弃区时
    // ── Wave2 反应式 watcher（队列派发，payload 带相关卡 id）──
    OnDonReturnedToDeck,    // 当(我方)咚!!放回咚!!卡组时
    OnCharRested,           // 当(任意)角色因效果转为休息状态时（payload: restedCardId, restedOwner）
    OnCharLeaveField,       // 当(任意)角色因效果离开场上时（payload: cardId, owner）
    OnLifeLeaveField,       // 当生命卡牌离开场上时（payload: owner）
    OnAllyCharEnter,        // 当我方(其它)角色登场时（payload: cardId, owner）
    OnOppEventPlayed,       // 当对方发动事件时（payload: owner=出牌方）
    OnOppBlocker,           // 当对方发动【阻挡者】时（payload: blockerOwner）
    OnAllyWillBeKOd,        // 守护者：他卡将要被KO时（payload: victimId, victimOwner）让"代替被KO"置换效果有机会发动
    OnAllyWillLeaveField,   // 守护者：我方角色因对方效果"将要离开场上"时（KO/退手牌/回卡组/置入生命，payload: victimId, victimOwner, kind）让"代替离场"置换效果有机会发动
    OnAnyCharKOd,           // 当(任意一方)角色被KO时（payload: cardId, owner, reason="battle"|"effect"）场上他卡可据此反应
    OnBattleEnd,            // 战斗结束时（在 EndBattle 清场前派发；CurrentBattle 仍可读，payload: attackerId/defenderPlayerIdx/targetCardId/targetIsLeader）
    OnTriggerActivated,     // 当(某方)发动生命【触发】时（payload: owner=发动方）——元触发，OP05-109
    OnHandDiscarded,        // 当(某方)手牌因效果被丢弃时（payload: owner）——OP14-056
    // 启动效果（玩家主动）
    ActivatedMain,          // 【启动主要】
    EventMain,              // 事件【主要】
    EventCounter,           // 事件【反击】
}
