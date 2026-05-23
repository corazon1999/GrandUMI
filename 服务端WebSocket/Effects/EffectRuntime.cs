using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects;

/// <summary>
/// 效果触发与解决的入口。
///
/// 卡牌效果的实现来源：
///   1. ScriptedEffectRegistry: 手写 C# 类（复杂卡 / 领航）
///   2. DslInterpreter:         OP15.json 中的声明式 DSL（约 80 张常规卡）
///
/// 触发顺序：回合玩家优先 → 非回合玩家 → 重复直到无新触发
/// </summary>
public static class EffectRuntime
{
    /// <summary>
    /// 在指定触发时机，对所有可响应的效果按规则顺序解析。
    /// 调用方应在状态变更后（出牌/攻击/KO/抽牌等）调用此方法。
    /// </summary>
    public static async Task TriggerEvent(GameState s, EffectTrigger trigger, IPromptService prompts, Dictionary<string, object?>? payload = null)
    {
        var candidates = CollectListeners(s, trigger, payload);
        // 排序：回合玩家在前，同方按场上从左到右
        candidates.Sort((a, b) =>
        {
            int aTurn = a.OwnerIdx == s.CurrentTurnPlayer ? 0 : 1;
            int bTurn = b.OwnerIdx == s.CurrentTurnPlayer ? 0 : 1;
            return aTurn.CompareTo(bTurn);
        });

        foreach (var c in candidates)
        {
            await Resolve(s, c.OwnerIdx, c.Source, trigger, prompts, payload);
            // 规则处理点：若已分胜负则中断
            if (s.IsGameOver) return;
        }
    }

    /// <summary>对单个卡牌的指定触发时机解析效果</summary>
    public static async Task Resolve(GameState s, int ownerIdx, CardInstance source, EffectTrigger trigger, IPromptService prompts, Dictionary<string, object?>? payload = null)
    {
        var ctx = new EffectContext
        {
            State = s,
            OwnerIndex = ownerIdx,
            Source = source,
            Trigger = trigger,
            Prompts = prompts,
            Engine = (prompts as PromptSystem)?.Engine,
        };
        if (payload is not null)
            foreach (var (k, v) in payload) ctx.Vars[k] = v;

        // 1. 优先用手写脚本
        var scripted = ScriptedEffectRegistry.TryGet(source.Info.Number);
        if (scripted is not null && scripted.HandlesTrigger(trigger))
        {
            await scripted.Resolve(ctx);
            return;
        }

        // 2. 退回 DSL
        await Dsl.DslInterpreter.TryResolve(ctx);
    }

    private record Candidate(int OwnerIdx, CardInstance Source);

    private static List<Candidate> CollectListeners(GameState s, EffectTrigger trigger, Dictionary<string, object?>? payload)
    {
        var list = new List<Candidate>();
        for (int i = 0; i < 2; i++)
        {
            var p = s.Players[i];
            // 领袖
            if (HasEffectForTrigger(p.Leader, trigger))
                list.Add(new(i, p.Leader));
            // 角色
            foreach (var c in p.Characters)
                if (HasEffectForTrigger(c, trigger))
                    list.Add(new(i, c));
            // 舞台
            if (p.StageCard is not null && HasEffectForTrigger(p.StageCard, trigger))
                list.Add(new(i, p.StageCard));
        }
        return list;
    }

    /// <summary>简单文本匹配判断卡牌是否含某触发时机的效果</summary>
    public static bool HasEffectForTrigger(CardInstance c, EffectTrigger t)
    {
        if (c.IsEffectsNullified) return false;
        var text = c.Info.EffectText ?? "";
        return t switch
        {
            EffectTrigger.OnEnterField        => text.Contains("【登场时】"),
            EffectTrigger.OnAttackDeclare     => text.Contains("【攻击时】"),
            EffectTrigger.OnOppAttackDeclare  => text.Contains("【对方的攻击时】"),
            EffectTrigger.OnBlockDeclare      => text.Contains("【阻挡时】"),
            EffectTrigger.OnKO                => text.Contains("【K.O.时】") || text.Contains("【KO时】"),
            EffectTrigger.PreKO               => text.Contains("不会被KO") || text.Contains("不会被K.O.") || text.Contains("将要被KO的场合") || text.Contains("将要被K.O.的场合") || text.Contains("将要被KO时") || text.Contains("被KO的场合"),
            EffectTrigger.OnMyTurnEnd         => text.Contains("【我方的回合结束时】"),
            EffectTrigger.OnOppTurnEnd        => text.Contains("【对方的回合结束时】"),
            EffectTrigger.ActivatedMain       => text.Contains("【启动主要】"),
            EffectTrigger.EventMain           => text.Contains("【主要】"),
            EffectTrigger.EventCounter        => text.Contains("【反击】"),
            EffectTrigger.OnLifeRevealTrigger => !string.IsNullOrEmpty(c.Info.Trigger),
            _ => false,
        };
    }
}

/// <summary>手写卡牌脚本接口</summary>
public interface IScriptedEffect
{
    string CardNumber { get; }
    bool HandlesTrigger(EffectTrigger trigger);
    Task Resolve(EffectContext ctx);
}

public static class ScriptedEffectRegistry
{
    private static readonly Dictionary<string, IScriptedEffect> _byNumber = new();
    private static bool _scanned;

    public static IScriptedEffect? TryGet(string number)
    {
        if (!_scanned) ScanAll();
        return _byNumber.TryGetValue(number, out var e) ? e : null;
    }

    public static void Register(IScriptedEffect effect) => _byNumber[effect.CardNumber] = effect;

    private static void ScanAll()
    {
        _scanned = true;
        // 反射扫描当前程序集中所有 IScriptedEffect 实现
        var asm = typeof(ScriptedEffectRegistry).Assembly;
        foreach (var t in asm.GetTypes())
        {
            if (t.IsAbstract || t.IsInterface) continue;
            if (!typeof(IScriptedEffect).IsAssignableFrom(t)) continue;
            try
            {
                var inst = (IScriptedEffect)Activator.CreateInstance(t)!;
                _byNumber[inst.CardNumber] = inst;
            }
            catch { /* 跳过构造失败的 */ }
        }
        Console.WriteLine($"[Effects] 已注册 {_byNumber.Count} 张手写卡效果");
    }
}
