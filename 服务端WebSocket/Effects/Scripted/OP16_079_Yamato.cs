using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-079 大和（领航）
/// 当我方从废弃区登场《和之国》角色时，本回合中该角色获得【速攻】
/// 通过 OnEnterField 配合"上次操作是 PlayFromTrash"的标记实现（M+1 引擎扩展更准确）
/// 当前占位：仅当本领航有此触发时机时，注册个一次性提示
/// </summary>
public class OP16_079_Yamato : IScriptedEffect
{
    public string CardNumber => "OP16-079";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;
    public Task Resolve(EffectContext ctx)
    {
        // 真正实现需要：PlayFromTrash 时把 Vars["FromTrash"] 写入；OnEnterField 检查
        // 当前占位：领航自身无 OnEnterField 触发，此方法实际不会跑（领航不"登场"）
        return Task.CompletedTask;
    }
}
