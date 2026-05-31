using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;

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
        LoadFile(path);
        _loaded = true;
    }

    /// <summary>批量加载某个目录下所有 *.json</summary>
    public static void LoadDirectory(string dir)
    {
        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"[DSL] 未找到定义目录: {dir}");
            _loaded = true;
            return;
        }
        int total = 0;
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            int n = LoadFile(file);
            total += n;
        }
        Console.WriteLine($"[DSL] 累计加载 {total} 条卡效定义，{_defs.Count} 张唯一卡");
        _loaded = true;
    }

    private static int LoadFile(string path)
    {
        if (!File.Exists(path)) return 0;
        try
        {
            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (dict is null) return 0;
            int n = 0;
            foreach (var (k, v) in dict)
            {
                _defs[k] = v;
                n++;
            }
            Console.WriteLine($"[DSL] {Path.GetFileName(path)} 加载 {n} 条");
            return n;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DSL] 加载 {path} 失败: {ex.Message}");
            return 0;
        }
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
        // 3. main: 事件主要（支持 cost 节）
        if (ctx.Trigger == EffectTrigger.EventMain && def.TryGetProperty("main", out var main))
        {
            if (CheckCondition(main, ctx) && await PayActivationCost(main, ctx) && main.TryGetProperty("then", out var then))
                await RunSteps(then, ctx);
        }
        // 4. trigger: 生命牌触发
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger && def.TryGetProperty("trigger", out var trig))
        {
            if (trig.ValueKind == JsonValueKind.Array) await RunSteps(trig, ctx);
        }
        // 5. activated: 【启动主要】（由 HandleUseEffect 触发 ActivatedMain）
        if (ctx.Trigger == EffectTrigger.ActivatedMain && def.TryGetProperty("activated", out var act))
        {
            if (CheckCondition(act, ctx) && CheckOncePerTurn(act, ctx) && await PayActivationCost(act, ctx))
            {
                if (act.TryGetProperty("then", out var then))
                {
                    await RunSteps(then, ctx);
                    MarkOncePerTurnUsed(act, ctx);
                }
            }
        }
        // 6. counter: 事件【反击】
        if (ctx.Trigger == EffectTrigger.EventCounter && def.TryGetProperty("counter", out var co))
        {
            if (co.ValueKind == JsonValueKind.Array) await RunSteps(co, ctx);
        }
        return true;
    }

    /// <summary>检查"【每回合 1 次】"占位是否已用</summary>
    static bool CheckOncePerTurn(JsonElement node, EffectContext ctx)
    {
        if (!node.TryGetProperty("oncePerTurn", out var k) || k.ValueKind != JsonValueKind.True) return true;
        var key = $"{ctx.Source.Info.Number}-Activated";
        return !ctx.State.Players[ctx.OwnerIndex].TurnOnceUsed.Contains(key);
    }
    static void MarkOncePerTurnUsed(JsonElement node, EffectContext ctx)
    {
        if (!node.TryGetProperty("oncePerTurn", out var k) || k.ValueKind != JsonValueKind.True) return;
        var key = $"{ctx.Source.Info.Number}-Activated";
        ctx.State.Players[ctx.OwnerIndex].TurnOnceUsed.Add(key);
    }

    /// <summary>支付 activated 节的 cost（咚!!-N / 自身放置废弃区等）</summary>
    static Task<bool> PayActivationCost(JsonElement node, EffectContext ctx)
    {
        if (!node.TryGetProperty("cost", out var cost) || cost.ValueKind != JsonValueKind.Object) return Task.FromResult(true);
        var me = ctx.State.Players[ctx.OwnerIndex];

        // donReturn: 咚!!-N（把活跃咚 N 张放回咚卡组）
        if (cost.TryGetProperty("donReturn", out var dr) && dr.ValueKind == JsonValueKind.Number)
        {
            int n = dr.GetInt32();
            int active = me.ActiveDonCount;
            if (active < n) return Task.FromResult(false);
            AtomicOps.ReturnDonToDeck(me, n);
        }

        // restSelf: 把自身转休息
        if (cost.TryGetProperty("restSelf", out var rs) && rs.ValueKind == JsonValueKind.True)
        {
            if (ctx.Source.IsTapped) return Task.FromResult(false);
            ctx.Source.IsTapped = true;
        }

        // selfToTrash: 把自身放置到废弃区
        if (cost.TryGetProperty("selfToTrash", out var st) && st.ValueKind == JsonValueKind.True)
        {
            // 自送废弃：从场上移除，不触发 KO
            BattleEngine.KOCard(ctx.State, ctx.OwnerIndex, ctx.Source);
        }

        return Task.FromResult(true);
    }

    static bool CheckCondition(JsonElement node, EffectContext ctx)
    {
        if (!node.TryGetProperty("if", out var ifNode)) return true;
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        var opp = s.Players[1 - ctx.OwnerIndex];
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
                case "leaderNameEquals":
                    if (me.Leader.Info.Name != p.Value.GetString()) return false;
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
                case "selfDonAttachedGte":
                    if (me.AttachedDonCount(ctx.Source.Id) < p.Value.GetInt32()) return false;
                    break;
                case "ownCharCountGte":
                    if (me.Characters.Count < p.Value.GetInt32()) return false;
                    break;
                case "oppCharCountGte":
                    if (opp.Characters.Count < p.Value.GetInt32()) return false;
                    break;
                case "ownLifeCountLte":
                    if (me.LifeCount > p.Value.GetInt32()) return false;
                    break;
                case "oppLifeCountLte":
                    if (opp.LifeCount > p.Value.GetInt32()) return false;
                    break;
                case "oppHandCountGte":
                    if (opp.Hand.Count < p.Value.GetInt32()) return false;
                    break;
                case "isMyTurn":
                    if ((s.CurrentTurnPlayer == ctx.OwnerIndex) != (p.Value.ValueKind == JsonValueKind.True)) return false;
                    break;
                case "donAttachedGteOpponent":
                    // 对方的目标角色有 ≥ N 张被赋予咚（用于"对方有被赋予中的咚的场合"）
                    int n = p.Value.GetInt32();
                    if (!opp.Characters.Any(c => opp.AttachedDonCount(c.Id) >= n)) return false;
                    break;
                case "lifeArousal":
                    // 【激起】简写：等价 ownLifeCountLte（生命 ≤ N 时该效果生效）
                    if (me.LifeCount > p.Value.GetInt32()) return false;
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

            // ── A 阶段 P0 新增 op ─────────────────────────────────────
            case "RefreshDon":
                {
                    var state = op.TryGetProperty("state", out var st) && st.GetString() == "rest"
                        ? DonState.Rest : DonState.Active;
                    AtomicOps.RefreshDonFromDeck(me, GetInt(op, "n", 1), state);
                    break;
                }
            case "ReturnToDeckBottom":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is null) break;
                    var from = op.TryGetProperty("from", out var fr) ? fr.GetString() : "field";
                    int owner = FindOwner(s, target);
                    var ownerP = owner >= 0 ? s.Players[owner] : me;
                    switch (from)
                    {
                        case "hand":  AtomicOps.ReturnHandToDeckBottom(ownerP, target); break;
                        case "trash": AtomicOps.ReturnTrashToDeckBottom(ownerP, target); break;
                        default:
                            if (owner >= 0) AtomicOps.ReturnFieldToDeckBottom(s, owner, target);
                            break;
                    }
                    break;
                }
            case "PlayFromTrash":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is null) break;
                    bool rest = op.TryGetProperty("rest", out var rv) && rv.ValueKind == JsonValueKind.True;
                    AtomicOps.PlayFromTrashFree(s, ctx.OwnerIndex, target, rest);
                    break;
                }
            case "TrashToHand":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is null) break;
                    AtomicOps.TrashToHand(me, target);
                    break;
                }
            case "SetPower":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is null) break;
                    int owner = FindOwner(s, target);
                    var ownerP = owner >= 0 ? s.Players[owner] : me;
                    int donCount = ownerP.AttachedDonCount(target.Id);
                    bool ownerTurn = owner == s.CurrentTurnPlayer;
                    AtomicOps.SetPowerThisTurn(target, GetInt(op, "value", 0), donCount, ownerTurn);
                    break;
                }
            case "OpponentDiscard":
                {
                    // DSL 中 op 是同步 switch，但内部可 await
                    if (ctx.Engine is not null)
                        await AtomicOps.OpponentDiscardChosen(ctx.Engine, 1 - ctx.OwnerIndex, GetInt(op, "n", 1));
                    break;
                }
            case "MarkPreventKO":
                {
                    var target = ResolveTarget(op, "target", ctx) ?? ctx.Source;
                    s.MarkPreventKO(target.Id);
                    break;
                }
            case "DiscardHand":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is null) break;
                    int owner = FindOwner(s, target);
                    var ownerP = owner >= 0 ? s.Players[owner] : me;
                    if (ownerP.Hand.Contains(target))
                        AtomicOps.DiscardHand(ownerP, target);
                    break;
                }

            // ── B 阶段 P1 新增 op ─────────────────────────────────────
            case "AddPowerAll":
                {
                    int sideIdx = op.TryGetProperty("side", out var sd) && sd.GetString() == "opp"
                        ? 1 - ctx.OwnerIndex : ctx.OwnerIndex;
                    int delta = GetInt(op, "delta", 0);
                    bool inclLeader = !op.TryGetProperty("excludeLeader", out var el) || el.ValueKind != JsonValueKind.True;
                    var filter = BuildCardFilter(op.TryGetProperty("filter", out var f) ? f : default);
                    AtomicOps.AddPowerToAllThisTurn(s, sideIdx, filter, delta, inclLeader);
                    break;
                }
            case "AddLifeFromDeck":
                AtomicOps.AddLifeFromDeckTop(me, GetInt(op, "n", 1));
                break;
            case "MoveCharToLife":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is null) break;
                    int owner = FindOwner(s, target);
                    if (owner >= 0) AtomicOps.MoveCharToLife(s, owner, target, toTop: true);
                    break;
                }
            case "SearchDeck":
                {
                    if (ctx.Engine is null) break;
                    var filter = BuildCardFilter(op.TryGetProperty("filter", out var f) ? f : default);
                    var text = op.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "从卡组选 1 张加入手牌";
                    var picked = await AtomicOps.SearchDeck(ctx.Engine, ctx.OwnerIndex, filter, text);
                    if (op.TryGetProperty("as", out var asN)) ctx.Vars[asN.GetString() ?? "$c"] = picked;
                    break;
                }
            case "AddCostMod":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is null) break;
                    AtomicOps.AddCostModifier(target, GetInt(op, "delta", 0), GetDuration(op));
                    break;
                }
            case "Nullify":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is null) break;
                    AtomicOps.NullifyEffects(target, GetDuration(op));
                    break;
                }
            case "AddRestriction":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is null) break;
                    var kindStr = op.TryGetProperty("kind", out var ks) ? ks.GetString() : null;
                    if (Enum.TryParse<RestrictionKind>(kindStr, out var kind))
                        AtomicOps.AddRestriction(target, kind, GetDuration(op));
                    break;
                }

            // ── C 阶段新增 ───────────────────────────────────────────
            case "LookOpponentHand":
                {
                    // 让自己看对手手牌（选取若干张丢弃 / 不操作仅查看）
                    int max = GetInt(op, "max", 0);
                    var oppHand = opp.Hand;
                    if (oppHand.Count == 0) break;
                    var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentHand",
                        max > 0 ? $"查看对手手牌（最多选 {max} 张丢弃）" : "查看对手手牌",
                        oppHand.Select(c => c.Id.ToString()).ToList(), 0, Math.Max(max, 1));
                    if (max > 0)
                    {
                        foreach (var cid in chosen)
                        {
                            var card = oppHand.FirstOrDefault(c => c.Id.ToString() == cid);
                            if (card is not null) AtomicOps.DiscardHand(opp, card);
                        }
                    }
                    break;
                }
            case "ShuffleDeck":
                AtomicOps.Shuffle(me.Deck);
                break;
            case "RevealLifeFaceUp":
                // 将我方/对方生命牌正面化（暂未在 PlayerState 中区分朝向，作为占位）
                _ = op;
                break;
        }
    }

    /// <summary>解析 DSL 中的 filter 节点，返回卡牌过滤谓词</summary>
    static Func<CardInstance, bool> BuildCardFilter(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object) return _ => true;
        return c =>
        {
            if (node.TryGetProperty("kind", out var k))
            {
                var kindStr = k.GetString();
                if (kindStr == "Character" && c.Info.Kind != CardKind.Character) return false;
                if (kindStr == "Event"     && c.Info.Kind != CardKind.Event)     return false;
                if (kindStr == "Stage"     && c.Info.Kind != CardKind.Stage)     return false;
            }
            if (node.TryGetProperty("keyword", out var kw))
            {
                var k2 = kw.GetString() ?? "";
                if (!c.Info.HasKeyword(k2)) return false;
            }
            if (node.TryGetProperty("originalCostLte", out var oc) && c.Info.Cost > oc.GetInt32()) return false;
            if (node.TryGetProperty("originalCostGte", out var oc2) && c.Info.Cost < oc2.GetInt32()) return false;
            if (node.TryGetProperty("originalPowerLte", out var pp) && c.Info.Power > pp.GetInt32()) return false;
            if (node.TryGetProperty("originalPowerGte", out var pp2) && c.Info.Power < pp2.GetInt32()) return false;
            if (node.TryGetProperty("nameEquals", out var nm) && !c.MatchesName(nm.GetString() ?? "")) return false;
            return true;
        };
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
            "OpponentCharacterWithDonGe2" => opp.Characters.Where(c => opp.AttachedDonCount(c.Id) >= 2).ToList(),
            "OpponentRestingCharacter"    => opp.Characters.Where(c => c.IsTapped).ToList(),
            "OpponentLeaderOrCharacter"   => new List<CardInstance> { opp.Leader }.Concat(opp.Characters).ToList(),
            "OwnCharacter"                => me.Characters.ToList(),
            "OwnLeaderOrCharacter"        => new List<CardInstance> { me.Leader }.Concat(me.Characters).ToList(),
            // P0/P1 新增
            "OwnHandCharacter"            => me.Hand.Where(c => c.Info.Kind == CardKind.Character).ToList(),
            "OwnHandEvent"                => me.Hand.Where(c => c.Info.Kind == CardKind.Event).ToList(),
            "OwnHand"                     => me.Hand.ToList(),
            "OwnTrashCharacter"           => me.Trash.Where(c => c.Info.Kind == CardKind.Character).ToList(),
            "OwnTrashEvent"               => me.Trash.Where(c => c.Info.Kind == CardKind.Event).ToList(),
            "OwnTrash"                    => me.Trash.ToList(),
            "OwnStage"                    => me.StageCard is { } sc ? new List<CardInstance> { sc } : new(),
            "AnyStage"                    => new[] { me.StageCard, opp.StageCard }.Where(c => c is not null).Cast<CardInstance>().ToList(),
            "OpponentCharacterCostLe5"    => opp.Characters.Where(c => c.Info.Cost <= 5).ToList(),
            // C2 看对手私有区域：候选 ID 仍是卡 GUID，但仅向查看方暴露
            "OpponentHand"                => opp.Hand.ToList(),
            "OpponentLifeAll"             => opp.LifeArea.ToList(),
            _                              => opp.Characters.ToList(),
        };
    }
}
