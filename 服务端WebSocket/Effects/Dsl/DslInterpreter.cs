using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Dsl;

/// <summary>
/// 把 Effects/Definitions/OP15.json 中的声明转换为对 AtomicOps 的调用
///
/// JSON 结构示例：
/// {
///   "OP15-004": {
///     "triggers": [{
///       "on": "OnEnterField",
///       "if": { "leaderPowerNotMoreThan": 0 },
///       "then": [
///         { "op": "Choose", "prompt": "OpponentCharacter", "max": 1, "as": "$tgt" },
///         { "op": "AddPowerThisTurn", "target": "$tgt", "delta": -3000 }
///       ]
///     }]
///   }
/// }
/// </summary>
public static class DslInterpreter
{
    private static Dictionary<string, JsonElement> _defs = new();
    private static bool _loaded;

    public static void Load(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"[DSL] 未找到定义文件: {path}");
            _loaded = true;
            return;
        }
        try
        {
            var json = File.ReadAllText(path);
            _defs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
            Console.WriteLine($"[DSL] 已加载 {_defs.Count} 条卡效定义");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DSL] 加载失败: {ex.Message}");
        }
        _loaded = true;
    }

    public static async Task<bool> TryResolve(EffectContext ctx)
    {
        if (!_loaded) return false;
        if (!_defs.TryGetValue(ctx.Source.Info.Number, out var def)) return false;

        var triggerName = ctx.Trigger.ToString();
        // 1. triggers: 数组里找 on == 当前触发的项
        if (def.TryGetProperty("triggers", out var triggers) && triggers.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in triggers.EnumerateArray())
            {
                if (t.TryGetProperty("on", out var on) && on.GetString() == triggerName)
                {
                    if (!CheckCondition(t, ctx)) continue;
                    await RunSteps(t.GetProperty("then"), ctx);
                }
            }
        }
        // 2. activated: 启动主要效果（M3 通过 UseEffect 入口触发）
        // 3. main: 事件主要
        if (ctx.Trigger == EffectTrigger.EventMain && def.TryGetProperty("main", out var main))
        {
            if (CheckCondition(main, ctx) && main.TryGetProperty("then", out var then))
                await RunSteps(then, ctx);
        }
        // 4. trigger: 生命牌触发
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger && def.TryGetProperty("trigger", out var trig))
        {
            if (trig.ValueKind == JsonValueKind.Array) await RunSteps(trig, ctx);
        }
        return true;
    }

    static bool CheckCondition(JsonElement node, EffectContext ctx)
    {
        if (!node.TryGetProperty("if", out var ifNode)) return true;
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        foreach (var p in ifNode.EnumerateObject())
        {
            switch (p.Name)
            {
                case "leaderPowerNotMoreThan":
                    int threshold = p.Value.GetInt32();
                    int curr = me.Leader.CurrentPower(me.AttachedDonCount(me.Leader.Id), s.CurrentTurnPlayer == ctx.OwnerIndex);
                    if (curr > threshold) return false;
                    break;
                case "leaderHasKeyword":
                    if (!me.Leader.Info.HasKeyword(p.Value.GetString() ?? "")) return false;
                    break;
                case "trashCountGte":
                    if (me.Trash.Count < p.Value.GetInt32()) return false;
                    break;
                case "trashEventCountGte":
                    if (me.Trash.Count(c => c.Info.Kind == CardKind.Event) < p.Value.GetInt32()) return false;
                    break;
                case "donAttachedGte":
                    if (me.AttachedDonCount(ctx.Source.Id) < p.Value.GetInt32()) return false;
                    break;
            }
        }
        return true;
    }

    static async Task RunSteps(JsonElement steps, EffectContext ctx)
    {
        if (steps.ValueKind != JsonValueKind.Array) return;
        foreach (var step in steps.EnumerateArray())
        {
            await RunOp(step, ctx);
            if (ctx.State.IsGameOver) return;
        }
    }

    static async Task RunOp(JsonElement op, EffectContext ctx)
    {
        var name = op.GetProperty("op").GetString();
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];

        switch (name)
        {
            case "Draw":
                AtomicOps.Draw(s, ctx.OwnerIndex, GetInt(op, "n", 1));
                break;
            case "MillTop":
                AtomicOps.MillTop(me, GetInt(op, "n", 1));
                break;
            case "AddPowerThisTurn":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is not null) AtomicOps.AddPowerThisTurn(target, GetInt(op, "delta", 0));
                    break;
                }
            case "AddPowerThisBattle":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is not null) AtomicOps.AddPowerThisBattle(target, GetInt(op, "delta", 0));
                    break;
                }
            case "KO":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is not null)
                    {
                        int owner = FindOwner(s, target);
                        if (owner >= 0) AtomicOps.KO(s, owner, target);
                    }
                    break;
                }
            case "Rest":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is not null) AtomicOps.RestCard(target);
                    break;
                }
            case "Activate":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is not null) AtomicOps.ActivateCard(target);
                    break;
                }
            case "GiveKeyword":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is not null)
                    {
                        var kw = op.GetProperty("keyword").GetString() ?? "";
                        var dur = GetDuration(op);
                        AtomicOps.GiveKeyword(target, kw, dur);
                    }
                    break;
                }
            case "AttachDon":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is null) break;
                    int n = GetInt(op, "n", 1);
                    int owner = FindOwner(s, target);
                    if (owner >= 0)
                    {
                        var state = op.TryGetProperty("from", out var f) && f.GetString() == "rest" ? DonState.Rest : DonState.Active;
                        AtomicOps.AttachDonFromCost(s.Players[owner], target.Id, n, state);
                    }
                    break;
                }
            case "Choose":
                {
                    string promptKind = op.GetProperty("prompt").GetString() ?? "OpponentCharacter";
                    int max = GetInt(op, "max", 1);
                    int min = GetInt(op, "min", 0);
                    var candidates = BuildCandidates(promptKind, ctx);
                    var text = op.TryGetProperty("text", out var tx) ? tx.GetString() ?? promptKind : promptKind;
                    var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, promptKind, text,
                        candidates.Select(c => c.Id.ToString()).ToList(), min, max);
                    var varName = op.TryGetProperty("as", out var v) ? v.GetString() : "$tgt";
                    if (chosen.Count > 0)
                    {
                        var picked = candidates.First(c => c.Id.ToString() == chosen[0]);
                        ctx.Vars[varName!] = picked;
                    }
                    else
                    {
                        ctx.Vars[varName!] = null;
                    }
                    break;
                }
            case "BounceToHand":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is not null)
                    {
                        int owner = FindOwner(s, target);
                        if (owner >= 0) AtomicOps.BounceToHand(s, owner, target);
                    }
                    break;
                }
            case "PreventActivateNextReset":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is not null) AtomicOps.PreventActivateNextReset(target);
                    break;
                }
            case "ReturnDonToDeck":
                AtomicOps.ReturnDonToDeck(me, GetInt(op, "n", 1));
                break;
        }
    }

    static int GetInt(JsonElement e, string key, int def)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : def;

    static KeywordDuration GetDuration(JsonElement op)
    {
        if (!op.TryGetProperty("duration", out var d)) return KeywordDuration.ThisTurn;
        return d.GetString() switch
        {
            "ThisBattle" => KeywordDuration.ThisBattle,
            "UntilNextOpponentEndPhase" => KeywordDuration.UntilNextOpponentEndPhase,
            _ => KeywordDuration.ThisTurn,
        };
    }

    static CardInstance? ResolveTarget(JsonElement op, string key, EffectContext ctx)
    {
        if (!op.TryGetProperty(key, out var t)) return null;
        if (t.ValueKind != JsonValueKind.String) return null;
        var name = t.GetString() ?? "";
        if (name.StartsWith("$"))
        {
            return ctx.Vars.TryGetValue(name, out var v) ? v as CardInstance : null;
        }
        return name switch
        {
            "self"        => ctx.Source,
            "selfLeader"  => ctx.State.Players[ctx.OwnerIndex].Leader,
            "oppLeader"   => ctx.State.Players[1 - ctx.OwnerIndex].Leader,
            _ => null,
        };
    }

    static int FindOwner(GameState s, CardInstance c)
    {
        for (int i = 0; i < 2; i++)
        {
            var p = s.Players[i];
            if (p.Leader == c) return i;
            if (p.Characters.Contains(c)) return i;
            if (p.StageCard == c) return i;
        }
        return -1;
    }

    static List<CardInstance> BuildCandidates(string promptKind, EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];
        return promptKind switch
        {
            "OpponentCharacter"           => opp.Characters.ToList(),
            "OpponentCharacterWithDon"    => opp.Characters.Where(c => opp.AttachedDonCount(c.Id) >= 1).ToList(),
            "OpponentRestingCharacter"    => opp.Characters.Where(c => c.IsTapped).ToList(),
            "OpponentLeaderOrCharacter"   => new List<CardInstance> { opp.Leader }.Concat(opp.Characters).ToList(),
            "OwnCharacter"                => me.Characters.ToList(),
            "OwnLeaderOrCharacter"        => new List<CardInstance> { me.Leader }.Concat(me.Characters).ToList(),
            _                              => opp.Characters.ToList(),
        };
    }
}
