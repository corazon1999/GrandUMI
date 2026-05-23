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
    OnKO,                   // 【K.O.时】
    OnDamageToLeader,       // 给对方领袖造成伤害时
    OnLifeRevealTrigger,    // 生命牌触发
    OnTurnStart,            // 回合开始时
    OnMyTurnEnd,            // 【我方的回合结束时】
    OnOppTurnEnd,           // 【对方的回合结束时】
    OnDonAttached,          // 咚被赋予时
    OnDrawCard,             // 抽牌时
    OnPlayCard,             // 出牌时（与 OnEnterField 不同：事件也算 play）
    OnEnterTrash,           // 进入废弃区时
    // 启动效果（玩家主动）
    ActivatedMain,          // 【启动主要】
    EventMain,              // 事件【主要】
    EventCounter,           // 事件【反击】
}
