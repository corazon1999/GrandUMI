using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;

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
    private static readonly object _loadLock = new();

    public static void Load(string path)
    {
        LoadFile(path);
        _loaded = true;
    }

    /// <summary>批量加载某个目录下所有 *.json。幂等且线程安全：首次加载后重复调用直接返回。</summary>
    public static void LoadDirectory(string dir)
    {
        if (_loaded) return;
        lock (_loadLock)
        {
            if (_loaded) return;
            LoadDirectoryCore(dir);
            _loaded = true;
        }
    }

    private static void LoadDirectoryCore(string dir)
    {
        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"[DSL] 未找到定义目录: {dir}");
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
                // 占位条目（仅 complex/noEffect 标记、无真实效果键）不得覆盖已加载的真实定义。
                // 定义目录按 Directory.GetFiles 枚举顺序加载，该顺序在 Linux 生产机不保证字母序，
                // 占位文件(如 *_gap.json)可能后加载冲掉真实定义(如 *_wf.json)，导致线上卡效失效。
                if (_defs.ContainsKey(k) && IsPlaceholderDef(v)) continue;
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

    /// <summary>判断定义条目是否为"占位"（仅含 complex/noEffect 标记、无任何真实效果键）。
    /// 占位用于标记"已知但未实现/无效果"的卡，不应覆盖其它文件里的真实定义。</summary>
    static bool IsPlaceholderDef(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return false;
        if (v.TryGetProperty("triggers", out _) || v.TryGetProperty("trigger", out _)
            || v.TryGetProperty("activated", out _) || v.TryGetProperty("main", out _)
            || v.TryGetProperty("counter", out _)) return false;
        bool complex  = v.TryGetProperty("complex",  out var c)  && c.ValueKind  == JsonValueKind.True;
        bool noEffect = v.TryGetProperty("noEffect", out var ne) && ne.ValueKind == JsonValueKind.True;
        return complex || noEffect;
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
                    if (!CheckTriggerOncePerTurn(t, ctx, triggerName)) continue;
                    // 带成本的触发效果（如【登场时】横置1咚：…）在 OPTCG 中为可选成本。
                    //   - 不能支付 → 静默跳过，不打扰玩家；
                    //   - 静默自动支付类成本（咚返还/横置等无独立选择界面）→ 先弹确认，玩家可放弃；
                    //   - 交互类成本（丢弃指定手牌/公开/放置角色等）自带「选0张=放弃」，不再额外确认以免双重弹窗。
                    if (t.TryGetProperty("cost", out var tcost) && tcost.ValueKind == JsonValueKind.Object)
                    {
                        if (!CanPayActivationCost(t, ctx)) continue;
                        if (CostNeedsConfirm(tcost))
                        {
                            var ask = (t.TryGetProperty("costPrompt", out var cp) ? cp.GetString() : null)
                                      ?? "是否支付成本以发动此效果？";
                            if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, ask)) continue;
                        }
                    }
                    if (!await PayActivationCost(t, ctx)) continue;
                    await RunSteps(t.GetProperty("then"), ctx);
                    MarkTriggerOncePerTurnUsed(t, ctx, triggerName);
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
        // 4. trigger: 生命牌触发（数组=直接执行；对象=支持 cost/if 节，复用 PayActivationCost，同 counter）
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger && def.TryGetProperty("trigger", out var trig))
        {
            if (trig.ValueKind == JsonValueKind.Array)
            {
                await RunSteps(trig, ctx);
            }
            else if (trig.ValueKind == JsonValueKind.Object)
            {
                if (CheckCondition(trig, ctx) && await PayActivationCost(trig, ctx) && trig.TryGetProperty("then", out var tthen))
                    await RunSteps(tthen, ctx);
            }
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
        // 6. counter: 事件【反击】（数组=直接执行；对象=支持 cost/if 节，复用 PayActivationCost）
        if (ctx.Trigger == EffectTrigger.EventCounter && def.TryGetProperty("counter", out var co))
        {
            if (co.ValueKind == JsonValueKind.Array)
            {
                await RunSteps(co, ctx);
            }
            else if (co.ValueKind == JsonValueKind.Object)
            {
                if (CheckCondition(co, ctx) && await PayActivationCost(co, ctx) && co.TryGetProperty("then", out var cthen))
                    await RunSteps(cthen, ctx);
            }
        }
        return true;
    }

    /// <summary>检查"【每回合 1 次】"占位是否已用</summary>
    static bool CheckOncePerTurn(JsonElement node, EffectContext ctx)
    {
        if (!node.TryGetProperty("oncePerTurn", out var k) || k.ValueKind != JsonValueKind.True) return true;
        var key = $"{ctx.Source.Id}-Activated";
        return !ctx.State.Players[ctx.OwnerIndex].TurnOnceUsed.Contains(key);
    }
    static void MarkOncePerTurnUsed(JsonElement node, EffectContext ctx)
    {
        if (!node.TryGetProperty("oncePerTurn", out var k) || k.ValueKind != JsonValueKind.True) return;
        var key = $"{ctx.Source.Id}-Activated";
        ctx.State.Players[ctx.OwnerIndex].TurnOnceUsed.Add(key);
    }

    /// <summary>triggers 节的「【每回合 1 次】」：按 卡实例+触发时机 各自计数，未发动前不消耗。</summary>
    static bool CheckTriggerOncePerTurn(JsonElement node, EffectContext ctx, string triggerName)
    {
        if (!node.TryGetProperty("oncePerTurn", out var k) || k.ValueKind != JsonValueKind.True) return true;
        var key = $"{ctx.Source.Id}-Trigger-{triggerName}";
        return !ctx.State.Players[ctx.OwnerIndex].TurnOnceUsed.Contains(key);
    }
    static void MarkTriggerOncePerTurnUsed(JsonElement node, EffectContext ctx, string triggerName)
    {
        if (!node.TryGetProperty("oncePerTurn", out var k) || k.ValueKind != JsonValueKind.True) return;
        var key = $"{ctx.Source.Id}-Trigger-{triggerName}";
        ctx.State.Players[ctx.OwnerIndex].TurnOnceUsed.Add(key);
    }

    /// <summary>支付 activated 节的 cost（咚!!-N / 自身放置废弃区 / 丢弃手牌等）</summary>
    static async Task<bool> PayActivationCost(JsonElement node, EffectContext ctx)
    {
        if (!node.TryGetProperty("cost", out var cost) || cost.ValueKind != JsonValueKind.Object) return true;
        // 标记"正在支付成本"：监听方可按各自规则区分成本与收益阶段（OP12-040 两者均触发）
        EffectRuntime.PayingCost = true;
        try
        {
            return await PayActivationCostCore(cost, ctx);
        }
        finally
        {
            EffectRuntime.PayingCost = false;
        }
    }

    static async Task<bool> PayActivationCostCore(JsonElement cost, EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // donReturn: 咚!!-N（将指定数量的咚放回咚卡组，可放回任意状态：活跃/休息/附着）
        // 弹窗让玩家手选要放回哪几张；合格咚不足或玩家取消则不支付、不发动。
        if (cost.TryGetProperty("donReturn", out var dr) && dr.ValueKind == JsonValueKind.Number)
        {
            int n = dr.GetInt32();
            if (!await AtomicOps.PromptReturnDonToDeck(ctx, n)) return false;
        }

        if (cost.TryGetProperty("donReturnAtLeastOne", out var dra) && dra.ValueKind == JsonValueKind.True)
        {
            if (!await AtomicOps.PromptReturnAtLeastOneDonToDeck(ctx)) return false;
        }

        // restSelf: 把自身转休息
        if (cost.TryGetProperty("restSelf", out var rs) && rs.ValueKind == JsonValueKind.True)
        {
            if (ctx.Source.IsTapped) return false;
            ctx.Source.IsTapped = true;
        }

        // selfToTrash: 把自身放置到废弃区
        if (cost.TryGetProperty("selfToTrash", out var st) && st.ValueKind == JsonValueKind.True)
        {
            // 自送废弃：从场上移除，不触发 KO；但属"离开场上"，须派发离场 watcher
            // （反馈#224：OP16-056 自弃后 OP16-041 巴奇领袖"离场时"效果收不到事件）
            BattleEngine.KOCard(ctx.State, ctx.OwnerIndex, ctx.Source);
            if (ctx.Source.Info.Kind == CardKind.Character)
                EffectRuntime.NotifyWatcher(EffectTrigger.OnCharLeaveField,
                    new Dictionary<string, object?> { ["cardId"] = ctx.Source.Id.ToString(), ["owner"] = ctx.OwnerIndex });
        }

        // handDiscard: 丢弃我方 N 张手牌作为成本。
        //   数字形式 → 强制丢弃 N 张（玩家自选任意手牌）。手牌不足则无法支付。
        //   对象形式 {n, ...filter, text} → 「可以丢弃 N 张符合过滤的手牌」式可放弃成本：
        //     候选不足 N → 无法支付；玩家放弃(选不满 N) → 视为不支付不发动。
        if (cost.TryGetProperty("handDiscard", out var hd))
        {
            if (hd.ValueKind == JsonValueKind.Number)
            {
                int n = hd.GetInt32();
                if (me.Hand.Count < n) return false;
                await DiscardOwnHand(ctx, n);
            }
            else if (hd.ValueKind == JsonValueKind.Object)
            {
                int n = GetInt(hd, "n", 1);
                var pred = BuildMatchPredicate(hd);
                var cands = me.Hand.Where(pred).ToList();
                if (cands.Count < n) return false;
                var text = hd.TryGetProperty("text", out var tx) ? tx.GetString() ?? "丢弃手牌作为成本（可放弃）" : "丢弃手牌作为成本（可放弃）";
                var extra = new Dictionary<string, object?>
                {
                    ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                };
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardOwnChosen", text,
                    cands.Select(c => c.Id.ToString()).ToList(), 0, n, extra);
                if (chosen.Count < n) return false;
                foreach (var cid in chosen)
                {
                    var card = cands.FirstOrDefault(c => c.Id.ToString() == cid);
                    if (card is not null) AtomicOps.DiscardHand(me, card);
                }
            }
        }

        // restActiveDon: 将 N 张活跃咚转为休息状态（不归还到卡组）
        if (cost.TryGetProperty("restActiveDon", out var rad) && rad.ValueKind == JsonValueKind.Number)
        {
            int n = rad.GetInt32();
            if (me.ActiveDonCount < n) return false;   // 前置检查：活跃咚不足则不支付，避免部分休息
            int restCount = 0;
            foreach (var d in me.CostArea)
            {
                if (restCount >= n) break;
                if (d.State == DonState.Active && d.AttachedToCardId is null)
                {
                    d.State = DonState.Rest;
                    restCount++;
                }
            }
            if (restCount < n) return false;
        }

        // restCards: 将 N 张"卡牌"转为休息状态（活跃 领袖/角色/舞台/咚!! 混选）。
        //   数字形=强制成本；对象形 {n, optional:true}=「可以…」可放弃(放弃则不支付不发动)。
        if (cost.TryGetProperty("restCards", out var rcd))
        {
            int n = 0; bool rcOpt = false;
            if (rcd.ValueKind == JsonValueKind.Number) n = rcd.GetInt32();
            else if (rcd.ValueKind == JsonValueKind.Object)
            {
                n = GetInt(rcd, "n", 1);
                rcOpt = rcd.TryGetProperty("optional", out var ro) && ro.ValueKind == JsonValueKind.True;
            }
            if (n > 0 && !await AtomicOps.PromptRestOwnCards(ctx, n,
                $"将我方 {n} 张卡牌转为休息状态（{(rcOpt ? "可放弃，" : "")}可选活跃 领袖/角色/舞台/咚!!）", rcOpt)) return false;
        }

        // millTop: 从卡组顶废弃 N 张（用于某些特殊费用）
        if (cost.TryGetProperty("millTop", out var mt) && mt.ValueKind == JsonValueKind.Number)
        {
            int n = mt.GetInt32();
            if (me.Deck.Count < n) return false;
            AtomicOps.MillTop(me, n);
        }

        // lifeToHand: 将己方生命区最上方 1 张加入手牌（用于某些支付）
        if (cost.TryGetProperty("lifeToHand", out var lth) && lth.ValueKind == JsonValueKind.True)
        {
            if (ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) return false; // ST15-001：本回合禁止己方效果将生命入手
            if (me.LifeArea.Count == 0) return false;
            var top = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            me.Hand.Add(top);
        }

        // revealHand: 公开我方手牌中 1 张符合过滤条件的卡牌作为成本（"可以…公开…：…"）。
        //   候选 0 张 → 无法支付；玩家可放弃（跳过）→ 视为不支付；选择后向对手公开演出。
        //   候选卡随 extra.choiceCards 下发卡面（手牌等私有区需显式下发番号才能在客户端显示）。
        if (cost.TryGetProperty("revealHand", out var rev) && rev.ValueKind == JsonValueKind.Object)
        {
            int n = GetInt(rev, "n", 1);
            var pred = BuildMatchPredicate(rev);
            var cands = me.Hand.Where(pred).ToList();
            if (cands.Count < n) return false;
            var text = rev.TryGetProperty("text", out var tx) ? tx.GetString() ?? "选择要公开的手牌（可放弃）" : "选择要公开的手牌（可放弃）";
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RevealOwnHand", text,
                cands.Select(c => c.Id.ToString()).ToList(), 0, n, extra);
            if (chosen.Count < n) return false;
            var nums = chosen.Select(cid => cands.First(c => c.Id.ToString() == cid).Info.Number).ToList();
            ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, nums);
        }

        // handToDeckBottom: 将我方 N 张手牌放置到卡组最下方作为成本（"可以将手牌放卡组底：…"）。可放弃。
        if (cost.TryGetProperty("handToDeckBottom", out var htd))
        {
            int n = htd.ValueKind == JsonValueKind.Number ? htd.GetInt32() : GetInt(htd, "n", 1);
            var pred = htd.ValueKind == JsonValueKind.Object ? BuildMatchPredicate(htd) : (_ => true);
            var cands = me.Hand.Where(pred).ToList();
            if (cands.Count < n) return false;
            var extra = new Dictionary<string, object?>
            { ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList() };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandToDeckBottom",
                $"将 {n} 张手牌放置到卡组最下方（可放弃）", cands.Select(c => c.Id.ToString()).ToList(), 0, n, extra);
            if (chosen.Count < n) return false;
            foreach (var cid in chosen)
            {
                var card = cands.FirstOrDefault(c => c.Id.ToString() == cid);
                if (card is not null) AtomicOps.ReturnHandToDeckBottom(me, card);
            }
        }

        // trashOwnCharacter: 将我方场上 1 张符合过滤的角色放置废弃区作为成本（"可以…放置…：…"）。
        //   无候选 → 无法支付；玩家可放弃(min 0) → 视为不支付不发动；放置废弃沿用 selfToTrash 的 KOCard 通道。
        if (cost.TryGetProperty("trashOwnCharacter", out var toc) && toc.ValueKind == JsonValueKind.Object)
        {
            var pred = BuildMatchPredicate(toc);
            var cands = me.Characters.Where(pred).ToList();
            if (cands.Count == 0) return false;
            var text = toc.TryGetProperty("text", out var tx2) ? tx2.GetString() ?? "选择要放置废弃区的角色（可放弃）" : "选择要放置废弃区的角色（可放弃）";
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacterToTrash", text,
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count == 0) return false;
            var picked = cands.First(c => c.Id.ToString() == chosen[0]);
            BattleEngine.KOCard(ctx.State, ctx.OwnerIndex, picked);
            // 成本离场同属"离开场上"，派发离场 watcher（反馈#224 同类缺口）
            EffectRuntime.NotifyWatcher(EffectTrigger.OnCharLeaveField,
                new Dictionary<string, object?> { ["cardId"] = picked.Id.ToString(), ["owner"] = ctx.OwnerIndex });
        }

        // lifeFaceUp: 将我方生命区最上方 1 张翻至正面朝上作为成本（"可以将生命顶1张翻正：…"）。已正面/生命空则无法支付。
        if (cost.TryGetProperty("lifeFaceUp", out var lfu) && lfu.ValueKind == JsonValueKind.True)
        {
            if (me.LifeArea.Count == 0 || me.LifeArea[0].IsLifeFaceUp) return false;
            AtomicOps.FlipTopLifeFaceUp(me);
        }

        // leaderPowerThisTurn: 将我方(活跃)领袖本回合力量 +N(N可为负) 作为成本（OP07-006 领袖-5000）。需领袖活跃。
        if (cost.TryGetProperty("leaderPowerThisTurn", out var lpt) && lpt.ValueKind == JsonValueKind.Number)
        {
            if (me.Leader.IsTapped) return false;
            AtomicOps.AddPowerThisTurn(me.Leader, lpt.GetInt32());
        }

        // lifeToHandChoice: 将我方生命区"最上方或最下方"1 张加入手牌作为成本（玩家选 顶/底/放弃）。
        if (cost.TryGetProperty("lifeToHandChoice", out var lhc) && lhc.ValueKind == JsonValueKind.True)
        {
            if (ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) return false;
            if (me.LifeArea.Count == 0) return false;
            int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "将生命区1张卡牌加入手牌作为成本：", new[] { "最上方", "最下方", "放弃" });
            if (opt is < 0 or 2) return false; // 放弃/超时 → 不支付
            int li = opt == 0 ? 0 : me.LifeArea.Count - 1;
            var lc = me.LifeArea[li];
            me.LifeArea.RemoveAt(li);
            lc.IsLifeFaceUp = false;
            me.Hand.Add(lc);
        }

        // restOwnKeyworded: 将我方 1 张含指定特征的领袖/舞台转为休息状态作为成本（OP10-044/081/095「休息德莱斯罗兹领袖/舞台」）。
        if (cost.TryGetProperty("restOwnKeyworded", out var rok) && rok.ValueKind == JsonValueKind.Object)
        {
            var kw = rok.TryGetProperty("keyword", out var kwv) ? kwv.GetString() ?? "" : "";  // 空=不限特征(任意领袖/舞台)
            var rcands = new List<CardInstance>();
            if (!me.Leader.IsTapped && (kw == "" || me.Leader.Info.HasKeyword(kw))) rcands.Add(me.Leader);
            if (me.StageCard is not null && !me.StageCard.IsTapped && (kw == "" || me.StageCard.Info.HasKeyword(kw))) rcands.Add(me.StageCard);
            if (rcands.Count == 0) return false;
            var rchosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrStageToRest",
                kw == "" ? "将我方1张领袖或舞台转为休息状态（可放弃）" : $"将我方1张《{kw}》领袖或舞台转为休息状态（可放弃）",
                rcands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (rchosen.Count == 0) return false;
            AtomicOps.RestCard(rcands.First(c => c.Id.ToString() == rchosen[0]));
        }

        // bounceOwnChar: 将我方 N 张角色放回手牌作为成本（数字=任意角色；对象 {n,...过滤} 带筛选，如 OP10-056 费≥4 德莱斯罗兹）。
        if (cost.TryGetProperty("bounceOwnChar", out var boc))
        {
            int n = boc.ValueKind == JsonValueKind.Number ? boc.GetInt32() : GetInt(boc, "n", 1);
            var bpred = boc.ValueKind == JsonValueKind.Object ? BuildMatchPredicate(boc) : (Func<CardInstance, bool>)(_ => true);
            var bcands = me.Characters.Where(bpred).ToList();
            if (bcands.Count < n) return false;
            var bextra = new Dictionary<string, object?>
            { ["choiceCards"] = bcands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList() };
            var bchosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacterToBounce",
                $"将我方 {n} 张角色放回手牌（可放弃）", bcands.Select(c => c.Id.ToString()).ToList(), 0, n, bextra);
            if (bchosen.Count < n) return false;
            foreach (var cid in bchosen)
            {
                var card = bcands.FirstOrDefault(c => c.Id.ToString() == cid);
                if (card is not null) AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, card);
            }
        }

        // trashToDeckBottom: 将我方废弃区 N 张(按过滤)自选放回卡组最下方作为成本（EB03-043/OP07-080「废弃区2张含CP放回卡组底」）。
        if (cost.TryGetProperty("trashToDeckBottom", out var ttd) && ttd.ValueKind == JsonValueKind.Object)
        {
            int n = GetInt(ttd, "n", 1);
            var pred = BuildMatchPredicate(ttd);
            var tcands = me.Trash.Where(pred).ToList();
            if (tcands.Count < n) return false;
            var textra = new Dictionary<string, object?>
            { ["choiceCards"] = tcands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList() };
            var tchosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrashToDeckBottom",
                $"将废弃区 {n} 张卡牌放回卡组最下方（可放弃）", tcands.Select(c => c.Id.ToString()).ToList(), 0, n, textra);
            if (tchosen.Count < n) return false;
            foreach (var cid in tchosen)
            {
                var card = tcands.FirstOrDefault(c => c.Id.ToString() == cid);
                if (card is not null) AtomicOps.ReturnTrashToDeckBottom(me, card);
            }
        }

        // lifeFaceDown: 将我方生命区最上方 1 张翻至正面朝下作为成本（白星/鱼人岛系；生命空则不可付）。
        if (cost.TryGetProperty("lifeFaceDown", out var lfd) && lfd.ValueKind == JsonValueKind.True)
        {
            if (me.LifeArea.Count == 0) return false;
            me.LifeArea[0].IsLifeFaceUp = false;
        }

        // lifeToTrash: 将我方生命区最上方 1 张放置到废弃区作为成本（EB03-055）。
        if (cost.TryGetProperty("lifeToTrash", out var ltt) && ltt.ValueKind == JsonValueKind.True)
        {
            if (me.LifeArea.Count == 0) return false;
            var lc = me.LifeArea[0]; me.LifeArea.RemoveAt(0); lc.IsLifeFaceUp = false; me.Trash.Add(lc);
        }

        // returnOwnStage: 将我方 1 张舞台放回卡组最下方作为成本（可带 originalCost 过滤；可放弃）。
        if (cost.TryGetProperty("returnOwnStage", out var ros) && (ros.ValueKind == JsonValueKind.Object || ros.ValueKind == JsonValueKind.True))
        {
            if (me.StageCard is null) return false;
            bool sok = true;
            if (ros.ValueKind == JsonValueKind.Object)
            {
                if (ros.TryGetProperty("originalCostLte", out var ocl) && me.StageCard.Info.Cost > ocl.GetInt32()) sok = false;
                if (ros.TryGetProperty("originalCostGte", out var ocg) && me.StageCard.Info.Cost < ocg.GetInt32()) sok = false;
            }
            if (!sok) return false;
            var schosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnStageToDeckBottom",
                "将我方1张舞台放回卡组最下方（可放弃）", new List<string> { me.StageCard.Id.ToString() }, 0, 1);
            if (schosen.Count == 0) return false;
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, me.StageCard);
        }

        // selfToDeckBottom: 将此角色放回持有者卡组最下方作为成本（OP06-016）。
        if (cost.TryGetProperty("selfToDeckBottom", out var sdb) && sdb.ValueKind == JsonValueKind.True)
        {
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, ctx.Source);
        }

        // attachDonToNamed: 赋予我方 1 张指定卡名角色 N 张活跃咚作为成本（EB04-009 给「希尔巴兹·雷利」贴活跃咚）。
        if (cost.TryGetProperty("attachDonToNamed", out var adn) && adn.ValueKind == JsonValueKind.Object)
        {
            var nm = adn.TryGetProperty("name", out var nmv) ? nmv.GetString() ?? "" : "";
            int n = GetInt(adn, "n", 1);
            var targets = me.Characters.Where(c => c.MatchesName(nm)).ToList();
            if (targets.Count == 0 || me.ActiveDonCount < n) return false;
            CardInstance tgt;
            if (targets.Count == 1) tgt = targets[0];
            else
            {
                var ac = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnNamedCharForDon",
                    $"选择要赋予 {n} 张活跃咚的「{nm}」", targets.Select(c => c.Id.ToString()).ToList(), 1, 1);
                if (ac.Count == 0) return false;
                tgt = targets.First(c => c.Id.ToString() == ac[0]);
            }
            AtomicOps.AttachDonFromCost(me, tgt.Id, n, DonState.Active);
        }

        return true;
    }

    /// <summary>静默自动支付类成本（无独立选择界面）→ 触发型效果需先确认是否支付。
    /// 交互类成本（handDiscard 对象 / revealHand / trashOwnCharacter）自带放弃机制，不计入。</summary>
    static bool CostNeedsConfirm(JsonElement cost)
    {
        string[] silent = { "donReturn", "restSelf", "selfToTrash", "restActiveDon", "millTop", "lifeToHand", "lifeFaceUp", "leaderPowerThisTurn", "lifeFaceDown", "lifeToTrash", "selfToDeckBottom" };
        foreach (var k in silent)
            if (cost.TryGetProperty(k, out _)) return true;
        // handDiscard 数字形式=强制丢弃 N 张，属静默；对象形式自带选择放弃，不计入
        if (cost.TryGetProperty("handDiscard", out var hd) && hd.ValueKind == JsonValueKind.Number) return true;
        return false;
    }

    /// <summary>只读判定 cost 是否可支付（不产生任何状态变化），用于触发型效果在弹确认前预检：
    /// 不可支付则静默跳过，避免弹了确认却支付失败的空操作。</summary>
    static bool CanPayActivationCost(JsonElement node, EffectContext ctx)
    {
        if (!node.TryGetProperty("cost", out var cost) || cost.ValueKind != JsonValueKind.Object) return true;
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (cost.TryGetProperty("donReturn", out var dr) && dr.ValueKind == JsonValueKind.Number)
            // donReturn 实际支付走 PromptReturnDonToDeck，可放回活跃/休息/附着任意咚，
            // 故预检口径须用费用区总咚数而非仅活跃咚，否则高费角色登场后效果被误判不可支付而静默跳过。
            if (me.TotalDonInCostArea < dr.GetInt32()) return false;
        if (cost.TryGetProperty("donReturnAtLeastOne", out var dra) && dra.ValueKind == JsonValueKind.True
            && me.TotalDonInCostArea < 1) return false;

        if (cost.TryGetProperty("restSelf", out var rs) && rs.ValueKind == JsonValueKind.True)
            if (ctx.Source.IsTapped) return false;

        // selfToTrash: 自送废弃，恒可支付

        if (cost.TryGetProperty("handDiscard", out var hd))
        {
            if (hd.ValueKind == JsonValueKind.Number) { if (me.Hand.Count < hd.GetInt32()) return false; }
            else if (hd.ValueKind == JsonValueKind.Object)
            {
                int n = GetInt(hd, "n", 1);
                var pred = BuildMatchPredicate(hd);
                if (me.Hand.Count(pred) < n) return false;
            }
        }

        if (cost.TryGetProperty("restActiveDon", out var rad) && rad.ValueKind == JsonValueKind.Number)
            if (me.ActiveDonCount < rad.GetInt32()) return false;

        if (cost.TryGetProperty("millTop", out var mt) && mt.ValueKind == JsonValueKind.Number)
            if (me.Deck.Count < mt.GetInt32()) return false;

        if (cost.TryGetProperty("lifeToHand", out var lth) && lth.ValueKind == JsonValueKind.True)
        {
            if (ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) return false;
            if (me.LifeArea.Count == 0) return false;
        }

        if (cost.TryGetProperty("revealHand", out var rev) && rev.ValueKind == JsonValueKind.Object)
        {
            var pred = BuildMatchPredicate(rev);
            if (me.Hand.Count(pred) < GetInt(rev, "n", 1)) return false;
        }

        if (cost.TryGetProperty("handToDeckBottom", out var htd))
        {
            int n = htd.ValueKind == JsonValueKind.Number ? htd.GetInt32() : GetInt(htd, "n", 1);
            int have = htd.ValueKind == JsonValueKind.Object ? me.Hand.Count(BuildMatchPredicate(htd)) : me.Hand.Count;
            if (have < n) return false;
        }

        if (cost.TryGetProperty("trashOwnCharacter", out var toc) && toc.ValueKind == JsonValueKind.Object)
        {
            var pred = BuildMatchPredicate(toc);
            if (!me.Characters.Any(pred)) return false;
        }

        if (cost.TryGetProperty("lifeFaceUp", out var lfu) && lfu.ValueKind == JsonValueKind.True)
            if (me.LifeArea.Count == 0 || me.LifeArea[0].IsLifeFaceUp) return false;

        if (cost.TryGetProperty("leaderPowerThisTurn", out var lpt) && lpt.ValueKind == JsonValueKind.Number)
            if (me.Leader.IsTapped) return false;

        if (cost.TryGetProperty("lifeToHandChoice", out var lhc) && lhc.ValueKind == JsonValueKind.True)
        {
            if (ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) return false;
            if (me.LifeArea.Count == 0) return false;
        }

        if (cost.TryGetProperty("restOwnKeyworded", out var rok) && rok.ValueKind == JsonValueKind.Object)
        {
            var kw = rok.TryGetProperty("keyword", out var kwv) ? kwv.GetString() ?? "" : "";
            bool any = (!me.Leader.IsTapped && (kw == "" || me.Leader.Info.HasKeyword(kw)))
                       || (me.StageCard is not null && !me.StageCard.IsTapped && (kw == "" || me.StageCard.Info.HasKeyword(kw)));
            if (!any) return false;
        }

        if (cost.TryGetProperty("bounceOwnChar", out var boc))
        {
            int n = boc.ValueKind == JsonValueKind.Number ? boc.GetInt32() : GetInt(boc, "n", 1);
            int have = boc.ValueKind == JsonValueKind.Object ? me.Characters.Count(BuildMatchPredicate(boc)) : me.Characters.Count;
            if (have < n) return false;
        }

        if (cost.TryGetProperty("trashToDeckBottom", out var ttd) && ttd.ValueKind == JsonValueKind.Object)
        {
            var pred = BuildMatchPredicate(ttd);
            if (me.Trash.Count(pred) < GetInt(ttd, "n", 1)) return false;
        }

        if ((cost.TryGetProperty("lifeFaceDown", out var lfd) && lfd.ValueKind == JsonValueKind.True)
            || (cost.TryGetProperty("lifeToTrash", out var ltt) && ltt.ValueKind == JsonValueKind.True))
            if (me.LifeArea.Count == 0) return false;

        if (cost.TryGetProperty("returnOwnStage", out var ros) && (ros.ValueKind == JsonValueKind.Object || ros.ValueKind == JsonValueKind.True))
        {
            if (me.StageCard is null) return false;
            if (ros.ValueKind == JsonValueKind.Object)
            {
                if (ros.TryGetProperty("originalCostLte", out var ocl) && me.StageCard.Info.Cost > ocl.GetInt32()) return false;
                if (ros.TryGetProperty("originalCostGte", out var ocg) && me.StageCard.Info.Cost < ocg.GetInt32()) return false;
            }
        }

        if (cost.TryGetProperty("attachDonToNamed", out var adn) && adn.ValueKind == JsonValueKind.Object)
        {
            var nm = adn.TryGetProperty("name", out var nmv) ? nmv.GetString() ?? "" : "";
            if (!me.Characters.Any(c => c.MatchesName(nm)) || me.ActiveDonCount < GetInt(adn, "n", 1)) return false;
        }

        return true;
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
                    int curr = s.CurrentPowerOf(ctx.OwnerIndex, me.Leader);
                    if (curr > threshold) return false;
                    break;
                case "leaderHasKeyword":
                    if (!me.Leader.Info.HasKeyword(p.Value.GetString() ?? "")) return false;
                    break;
                case "leaderNameEquals":
                    if (!me.Leader.Info.NameIs(p.Value.GetString() ?? "")) return false;
                    break;
                case "leaderHasProperty":
                    // 多属性卡（如"斩/打"）按 / 拆分匹配，精确比较会漏掉（反馈#225）
                    if (!me.Leader.Info.Property.Split('/').Contains(p.Value.GetString() ?? "")) return false;
                    break;
                case "leaderColorIncludes":
                    if (!ColorMatches(me.Leader, p.Value.GetString() ?? "")) return false;
                    break;
                case "selfDonNotMoreThanOpp":
                    // 我方场上咚!!的张数不多于对方场上咚!!的张数
                    if ((me.TotalDonInCostArea <= opp.TotalDonInCostArea) != (p.Value.ValueKind == JsonValueKind.True)) return false;
                    break;
                case "attachedDonTotalGte":
                    // 我方合计存在 N 张或更多被赋予中的咚!!
                    if (me.CostArea.Count(d => d.State == DonState.Attached) < p.Value.GetInt32()) return false;
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
                // ── D 阶段新增条件 ──────────────────────────────────────
                case "selfDonTotalGte":
                    if (me.TotalDonInCostArea < p.Value.GetInt32()) return false;
                    break;
                case "selfDonTotalLte":
                    if (me.TotalDonInCostArea > p.Value.GetInt32()) return false;
                    break;
                case "oppDonTotalGte":
                    if (opp.TotalDonInCostArea < p.Value.GetInt32()) return false;
                    break;
                case "oppDonTotalLte":
                    if (opp.TotalDonInCostArea > p.Value.GetInt32()) return false;
                    break;
                case "selfHandCountGte":
                    if (me.Hand.Count < p.Value.GetInt32()) return false;
                    break;
                case "selfHandCountLte":
                    if (me.Hand.Count > p.Value.GetInt32()) return false;
                    break;
                case "isOppTurn":
                    if ((s.CurrentTurnPlayer != ctx.OwnerIndex) != (p.Value.ValueKind == JsonValueKind.True)) return false;
                    break;
                case "turnCountGte":
                    if (s.TurnCount < p.Value.GetInt32()) return false;
                    break;
                case "selfPowerGte":
                {
                    int sp = s.CurrentPowerOf(ctx.OwnerIndex, ctx.Source);
                    if (sp < p.Value.GetInt32()) return false;
                    break;
                }
                case "selfPowerLte":
                {
                    int sp = s.CurrentPowerOf(ctx.OwnerIndex, ctx.Source);
                    if (sp > p.Value.GetInt32()) return false;
                    break;
                }
                case "leaderColorCountGte":
                    if (me.Leader.Info.ColorList.Length < p.Value.GetInt32()) return false;
                    break;
                case "leaderPowerGte":
                {
                    int lp = s.CurrentPowerOf(ctx.OwnerIndex, me.Leader);
                    if (lp < p.Value.GetInt32()) return false;
                    break;
                }
                case "selfLifeCountGte":
                    if (me.LifeCount < p.Value.GetInt32()) return false;
                    break;
                case "oppLifeCountGte":
                    if (opp.LifeCount < p.Value.GetInt32()) return false;
                    break;
                case "donAttachedGteOwn":
                {
                    // 己方任意角色被赋予咚数 ≥ N
                    int nDon = p.Value.GetInt32();
                    if (!me.Characters.Any(c => me.AttachedDonCount(c.Id) >= nDon)) return false;
                    break;
                }
                case "donAttachedLteOwn":
                {
                    int nDon = p.Value.GetInt32();
                    if (!me.Characters.Any(c => me.AttachedDonCount(c.Id) <= nDon)) return false;
                    break;
                }
                case "ownTrashCountGte":
                    if (me.Trash.Count < p.Value.GetInt32()) return false;
                    break;
                case "oppTrashCountGte":
                    if (opp.Trash.Count < p.Value.GetInt32()) return false;
                    break;
                case "ownTrashEventCountGte":
                    if (me.Trash.Count(c => c.Info.Kind == CardKind.Event) < p.Value.GetInt32()) return false;
                    break;
                // ── B 阶段（OP09）新增条件 ──────────────────────────────
                case "ownRestedCharCountGte":
                    // 我方场上处于休息状态的角色数 ≥ N
                    if (me.Characters.Count(c => c.IsTapped) < p.Value.GetInt32()) return false;
                    break;
                case "bothLifeTotalLte":
                    // 双方生命卡牌合计张数 ≤ N
                    if (me.LifeCount + opp.LifeCount > p.Value.GetInt32()) return false;
                    break;
                // ── ST 阶段新增条件 ──────────────────────────────────────
                case "ownHasCharCostGte":
                    // 我方场上存在当前费用 ≥ N 的角色（包含临时及持续费用修正）。
                    if (!me.Characters.Any(c => s.CurrentCostOf(ctx.OwnerIndex, c) >= p.Value.GetInt32())) return false;
                    break;
                case "ownHasCharPowerGte":
                    // 我方场上存在当前力量 ≥ N 的角色（包含咚!!、临时修正及持续效果）。
                    if (!me.Characters.Any(c => s.CurrentPowerOf(ctx.OwnerIndex, c) >= p.Value.GetInt32())) return false;
                    break;
                case "oppHasCharPowerGte":
                    // 对方场上存在当前力量 ≥ N 的角色
                    if (!opp.Characters.Any(c => s.CurrentPowerOf(1 - ctx.OwnerIndex, c) >= p.Value.GetInt32())) return false;
                    break;
                case "ownHasCharWithKeyword":
                    // 我方场上存在拥有指定特征的角色（如 OP16-076 反击段"存在《大将》角色"）
                    if (!me.Characters.Any(c => c.Info.HasKeyword(p.Value.GetString() ?? ""))) return false;
                    break;
                case "ownOriginalPowerCharCountGte":
                {
                    // 我方场上原本力量等于指定值的角色数量不少于 count。
                    // 使用 Info.Power，避免咚!!或临时力量修正影响“原本力量”判定。
                    if (p.Value.ValueKind != JsonValueKind.Object) return false;
                    int power = GetInt(p.Value, "power", 0);
                    int count = GetInt(p.Value, "count", 1);
                    if (me.Characters.Count(c => c.Info.Power == power) < count) return false;
                    break;
                }
                case "ownHasNamedOriginalPowerChars":
                {
                    // 每项要求由不同角色满足，支持卡牌的静态别名与运行时名称别名。
                    if (p.Value.ValueKind != JsonValueKind.Array) return false;
                    var unmatched = me.Characters.ToList();
                    foreach (var requirement in p.Value.EnumerateArray())
                    {
                        string name = requirement.TryGetProperty("name", out var nv) ? nv.GetString() ?? "" : "";
                        int power = GetInt(requirement, "power", 0);
                        var match = unmatched.FirstOrDefault(c => c.MatchesName(name) && c.Info.Power == power);
                        if (match is null) return false;
                        unmatched.Remove(match);
                    }
                    break;
                }
                case "boardHasCharCostLte":
                    // 场上（双方）存在原本费用 ≤ N 的角色（“场上存在费用为0的角色”用 0）
                    if (!me.Characters.Concat(opp.Characters).Any(c => c.Info.Cost <= p.Value.GetInt32())) return false;
                    break;
                case "ownRestedCardCountGte":
                {
                    // 我方场上处于休息状态的卡牌张数（休息角色 + 休息咚 + 休息舞台）≥ N
                    int rested = me.Characters.Count(c => c.IsTapped)
                                 + me.CostArea.Count(d => d.State == DonState.Rest)
                                 + (me.StageCard is not null && me.StageCard.IsTapped ? 1 : 0);
                    if (rested < p.Value.GetInt32()) return false;
                    break;
                }
                case "oppRestedCharCountGte":
                    // 对方场上处于休息状态的角色数 ≥ N
                    if (opp.Characters.Count(c => c.IsTapped) < p.Value.GetInt32()) return false;
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
            // 步骤级条件：op 节点带 "if" 时不满足则跳过该步（对应卡面"之后，X的场合…"分句，如 OP05-019）
            if (!CheckCondition(step, ctx)) continue;
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
                    foreach (var target in ResolveTargets(op, "target", ctx))
                        AtomicOps.AddPowerThisTurn(target, GetInt(op, "delta", 0));
                    break;
                }
            case "AddPowerThisBattle":
                {
                    foreach (var target in ResolveTargets(op, "target", ctx))
                        AtomicOps.AddPowerThisBattle(target, GetInt(op, "delta", 0));
                    break;
                }
            case "AddPowerUntilOppEnd":
                {
                    // 力量±N "直到下个对方的结束阶段结束时为止"（如 OP16-065 对方角色力量-6000）
                    foreach (var target in ResolveTargets(op, "target", ctx))
                        AtomicOps.AddPowerUntilOppEnd(target, GetInt(op, "delta", 0), ctx.OwnerIndex);
                    break;
                }
            // 「原本的力量变为 N」（本回合）：写 OriginalPowerOverride，咚/加成照常叠加；回合末自动清。
            // 区别于 SetPower（"当前总力量变为N"差值法，会吞掉咚加成）。如 OP16-106。
            case "SetOriginalPower":
                {
                    foreach (var target in ResolveTargets(op, "target", ctx))
                        target.OriginalPowerOverride = GetInt(op, "value", 0);
                    break;
                }
            // 「我方所有符合过滤的角色原本力量变为 N」（本回合），如 OP16-058 囚犯全体变7000
            case "SetOriginalPowerAll":
                {
                    var sopPred = BuildMatchPredicate(op.TryGetProperty("filter", out var sopF) ? sopF : default);
                    foreach (var c in me.Characters.Where(sopPred))
                        c.OriginalPowerOverride = GetInt(op, "value", 0);
                    break;
                }
            // 「原本的力量变为 N，直到下个对方的结束阶段结束时为止」：以 delta = N - 卡面Power 经
            // AddPowerUntilOppEnd 近似（与库内"变为X"持续实现惯例一致）。如 ST26-005 草帽领袖变7000。
            case "SetOriginalPowerUntilOppEnd":
                {
                    foreach (var target in ResolveTargets(op, "target", ctx))
                        AtomicOps.AddPowerUntilOppEnd(target, GetInt(op, "value", 0) - target.Info.Power, ctx.OwnerIndex);
                    break;
                }
            // 「对方将其 N 张手牌放回卡组最下方」（对方自选，如 EB04-025）。手牌不足时按实有张数。
            case "OppHandToDeckBottom":
                {
                    int ohn = Math.Min(GetInt(op, "n", 1), opp.Hand.Count);
                    if (ohn == 0) break;
                    var ohExtra = new Dictionary<string, object?>
                    {
                        ["choiceCards"] = opp.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                    };
                    var ohChosen = await ctx.Prompts.ChooseCards(1 - ctx.OwnerIndex, "OwnHand",
                        $"将你的 {ohn} 张手牌放回卡组最下方",
                        opp.Hand.Select(c => c.Id.ToString()).ToList(), ohn, ohn, ohExtra);
                    foreach (var cid in ohChosen)
                    {
                        var picked = opp.Hand.FirstOrDefault(c => c.Id.ToString() == cid);
                        if (picked is not null) AtomicOps.ReturnHandToDeckBottom(opp, picked);
                    }
                    break;
                }
            case "KO":
                {
                    var targets = ResolveTargets(op, "target", ctx)
                        .DistinctBy(c => c.Id)
                        .ToList();
                    foreach (var ownerGroup in targets.GroupBy(target => FindOwner(s, target)))
                    {
                        int owner = ownerGroup.Key;
                        if (owner < 0) continue;
                        var cards = ownerGroup.ToList();
                        var characters = cards.Where(c => s.Players[owner].Characters.Contains(c)).ToList();
                        if (characters.Count > 0)
                            await AtomicOps.KOCardsByEffectAsync(s, owner, characters, ctx.Prompts, ctx.OwnerIndex);
                        // 舞台等非角色目标不属于批量角色 KO，仍走完整的单目标效果 KO。
                        foreach (var target in cards.Where(c => !characters.Contains(c)))
                            await AtomicOps.KOByEffectAsync(s, owner, target, ctx.Prompts, ctx.OwnerIndex);
                    }
                    break;
                }
            // 「将…放置到废弃区」（如 OP09-009）：非 KO——不触发【K.O.时】/PreKO/守护者置换/"不会被KO"保护，
            // 但仍受"不会离场"保护约束并派发离场 watcher。
            case "TrashTarget":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is not null)
                    {
                        int owner = FindOwner(s, target);
                        if (owner >= 0 && !s.IsLeaveGuarded(target, "effect"))
                        {
                            BattleEngine.KOCard(s, owner, target);
                            EffectRuntime.NotifyWatcher(EffectTrigger.OnCharLeaveField,
                                new Dictionary<string, object?> { ["cardId"] = target.Id.ToString(), ["owner"] = owner });
                        }
                    }
                    break;
                }
            case "Rest":
                {
                    foreach (var target in ResolveTargets(op, "target", ctx))
                        AtomicOps.RestCard(target);
                    break;
                }
            // RestOpponentCards: 将对方最多 N 张"卡牌"转为休息状态（活跃的 领袖/角色/舞台/咚!! 混选，对应"将对方N张卡牌转为休息"）
            case "RestOpponentCards":
                await AtomicOps.PromptRestOpponentCards(ctx, GetInt(op, "n", 1));
                break;
            case "Activate":
                {
                    foreach (var target in ResolveTargets(op, "target", ctx))
                        AtomicOps.ActivateCard(target);
                    break;
                }
            case "GiveKeyword":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is not null)
                    {
                        var kw = op.GetProperty("keyword").GetString() ?? "";
                        var dur = GetDuration(op);
                        // 传入控制者一方，使 UntilNextOpponentEndPhase 正确持续到对方结束阶段（#147）
                        AtomicOps.GiveKeyword(target, kw, dur, ctx.OwnerIndex);
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
                    var text = op.TryGetProperty("text", out var tx) ? tx.GetString() ?? promptKind : promptKind;
                    // 旧定义把未写“原本”的场上费用/力量也编码成 original*，并且部分旧 prompt
                    // 自带卡面费用预筛。场上效果默认按当前值；明确标注 valueBasis=original（旧定义
                    // 则兼容提示文本中的“原本”）时按卡面值。手牌/卡组/废弃区等非场上区域仍按卡面值。
                    string? valueBasis = op.TryGetProperty("valueBasis", out var vb) ? vb.GetString() : null;
                    bool explicitlyOriginal = string.Equals(valueBasis, "original", StringComparison.OrdinalIgnoreCase)
                        || (valueBasis is null && text.Contains("原本"));
                    bool fieldUsesCurrentValues = IsFieldCandidatePrompt(promptKind) && !explicitlyOriginal;
                    var candidates = BuildCandidates(promptKind, ctx, legacyCostUsesOriginal: explicitlyOriginal);
                    // 可选 filter：在 prompt 基础候选上再套一层卡牌过滤（力量/费用/特征/名称等，复用 BuildCardFilter）
                    if (op.TryGetProperty("filter", out var chFilter) && chFilter.ValueKind == JsonValueKind.Object)
                    {
                        var pred = BuildMatchPredicate(chFilter, s, fieldUsesCurrentValues);
                        candidates = candidates.Where(pred).ToList();

                        // 费用未写“原本的”时按当前费用判定。该判断需要对局与控制方上下文，
                        // 因而放在 Choose 阶段，而不是无状态的 BuildCardFilter 中。
                        if (chFilter.TryGetProperty("currentCostLte", out var ccl))
                        {
                            int limit = ccl.GetInt32();
                            candidates = candidates.Where(c => CurrentCostOfCard(c, s) <= limit).ToList();
                        }
                        if (chFilter.TryGetProperty("currentCostGte", out var ccg))
                        {
                            int limit = ccg.GetInt32();
                            candidates = candidates.Where(c => CurrentCostOfCard(c, s) >= limit).ToList();
                        }

                        // “拥有【阻挡者】效果”等条件按当前实际关键词判定，包含临时授予和持续效果。
                        if (chFilter.TryGetProperty("effectiveKeyword", out var ek))
                        {
                            string keyword = ek.GetString() ?? "";
                            candidates = candidates.Where(c => ActionValidator.HasKeyword(s, c, keyword)).ToList();
                        }
                    }
                    // 下发候选卡 {id,number}，让前端能解析"不在场上"区域（废弃区/卡组等）候选的卡图；
                    // 对场上候选也等价（前端 findCardById 优先用此映射），纯增益。
                    var chExtra = new Dictionary<string, object?>
                    {
                        ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                    };
                    var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, promptKind, text,
                        candidates.Select(c => c.Id.ToString()).ToList(), min, max, chExtra);
                    var varName = op.TryGetProperty("as", out var v) ? v.GetString() : "$tgt";
                    if (max > 1)
                    {
                        // 多选（最多N张）：存 List，消费端经 ResolveTargets 逐个应用（如 OP16-076 选最多3张各+2000）
                        ctx.Vars[varName!] = chosen
                            .Select(id => candidates.FirstOrDefault(c => c.Id.ToString() == id))
                            .Where(c => c is not null).Cast<CardInstance>().ToList();
                    }
                    else if (chosen.Count > 0)
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
                        if (owner >= 0 && !await AtomicOps.TryEffectLeaveGuard(s, owner, target, ctx.Prompts, "bounce"))
                            AtomicOps.BounceToHand(s, owner, target);
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
            case "OpponentReturnDonToDeck":
                await AtomicOps.PromptReturnDonToDeck(ctx, 1 - ctx.OwnerIndex,
                    GetInt(op, "n", 1), optional: false);
                break;

            // ── A 阶段 P0 新增 op ─────────────────────────────────────
            case "RefreshDon":
                {
                    var state = op.TryGetProperty("state", out var st) && st.GetString() == "rest"
                        ? DonState.Rest : DonState.Active;
                    int rdn = GetInt(op, "n", 1);
                    // optional:true → 卡面"追加最多N张"，玩家可选择不追加（如"不跳咚"）。
                    bool rdOptional = op.TryGetProperty("optional", out var rdo) && rdo.ValueKind == JsonValueKind.True;
                    if (rdOptional)
                    {
                        if (me.DonDeck.Count == 0 || me.TotalDonInCostArea >= TurnEngine.MaxDonInCostArea) break;
                        var rdText = op.TryGetProperty("text", out var rdt) ? rdt.GetString() : null;
                        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, rdText ?? $"是否从咚!!卡组追加{rdn}张咚!!？")) break;
                    }
                    AtomicOps.RefreshDonFromDeck(me, rdn, state);
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
                            if (owner >= 0 && !await AtomicOps.TryEffectLeaveGuard(s, owner, target, ctx.Prompts, "deck"))
                                AtomicOps.ReturnFieldToDeckBottom(s, owner, target);
                            break;
                    }
                    break;
                }
            case "PlayFromTrash":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is null) break;
                    bool rest = op.TryGetProperty("rest", out var rv) && rv.ValueKind == JsonValueKind.True;
                    await EnsureRoomForCharacter(ctx, ctx.OwnerIndex, target);
                    await AtomicOps.PlayFromTrashFree(s, ctx.OwnerIndex, target, rest);
                    break;
                }
            // PlaySelf:「此卡牌登场」生命触发——把此卡(发动触发时已被移入废弃区)从废弃区登场到场上(触发其登场时)。
            case "PlaySelf":
                if (ctx.Source.Info.Kind == CardKind.Character)
                {
                    await EnsureRoomForCharacter(ctx, ctx.OwnerIndex, ctx.Source);
                    await AtomicOps.PlayFromTrashFree(s, ctx.OwnerIndex, ctx.Source);
                }
                break;
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
                    // random:true → 随机弃（"丢弃对方N张手牌"措辞）；否则对方自选（"对方丢弃N张手牌"措辞）
                    if (ctx.Engine is not null)
                    {
                        int dn = GetInt(op, "n", 1);
                        if (op.TryGetProperty("random", out var rdm) && rdm.ValueKind == JsonValueKind.True)
                            AtomicOps.OpponentDiscardRandom(ctx.Engine, 1 - ctx.OwnerIndex, dn);
                        else
                            await AtomicOps.OpponentDiscardChosen(ctx.Engine, 1 - ctx.OwnerIndex, dn);
                    }
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
                    var filter = BuildMatchPredicate(op.TryGetProperty("filter", out var f) ? f : default);
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
                    if (owner >= 0 && !await AtomicOps.TryEffectLeaveGuard(s, owner, target, ctx.Prompts, "life"))
                        AtomicOps.MoveCharToLife(s, owner, target, toTop: true);
                    break;
                }
            case "HandToLife":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is not null)
                        AtomicOps.HandToLife(me, target,
                            toTop: !op.TryGetProperty("to", out var to) || to.GetString() != "bottom",
                            faceUp: op.TryGetProperty("faceUp", out var fu) && fu.ValueKind == JsonValueKind.True);
                    break;
                }
            case "SearchDeck":
                {
                    if (ctx.Engine is null) break;
                    var filter = BuildMatchPredicate(op.TryGetProperty("filter", out var f) ? f : default);
                    var text = op.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "从卡组选 1 张加入手牌";
                    var picked = await AtomicOps.SearchDeck(ctx.Engine, ctx.OwnerIndex, filter, text);
                    if (op.TryGetProperty("as", out var asN)) ctx.Vars[asN.GetString() ?? "$c"] = picked;
                    break;
                }
            case "AddCostMod":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is null) break;
                    AtomicOps.AddCostModifier(target, GetInt(op, "delta", 0), GetDuration(op), ctx.OwnerIndex);
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
                        AtomicOps.AddRestriction(target, kind, GetDuration(op), ctx.OwnerIndex);
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
            // ── 反馈#176：确认对方卡组最上方 count 张（私下、仅发动方可见，无后续操作）──
            //    复用通用 ChooseCards 只读展示通道：validChoices 留空(不可选)，choiceCards 携带牌面，
            //    快照按 PlayerIndex 过滤 extra → 只有发动方能看到牌面，对手看不到（"确认"语义）。
            case "LookOppTop":
                {
                    int count = GetInt(op, "count", 1);
                    int peek = Math.Min(count, opp.Deck.Count);
                    if (peek == 0) break;
                    var top = opp.Deck.Take(peek).ToList();
                    var extra = new Dictionary<string, object?>
                    {
                        ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                    };
                    // validChoices 空 + min/max=0 → 前端渲染为"仅供确认、不可选"的卡图，带跳过/确认按钮关闭。
                    await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookOppTop",
                        $"确认对方卡组最上方的 {peek} 张卡牌", new List<string>(), 0, 0, extra);
                    break;
                }
            case "ShuffleDeck":
                if (ctx.Engine is not null)
                    ctx.Engine.ShuffleDeck(me, ctx.OwnerIndex, "dsl_shuffle_deck");
                else
                    AtomicOps.Shuffle(ctx.State, me.Deck);
                break;
            case "RevealLifeFaceUp":
                // 将我方/对方生命牌正面化（暂未在 PlayerState 中区分朝向，作为占位）
                _ = op;
                break;

            // ── 通用"探顶"：确认卡组顶 count 张，公开符合 match 的最多 max 张加入手牌，
            //    其余按原相对顺序放回卡组最下方(restTo=bottom)或废弃区(restTo=trash) ──
            case "LookTopReveal":
                {
                    int count = GetInt(op, "count", 5);
                    int max = GetInt(op, "max", 1);
                    string restTo = op.TryGetProperty("restTo", out var rt) ? rt.GetString() ?? "bottom" : "bottom";
                    var match = BuildMatchPredicate(op.TryGetProperty("match", out var mm) ? mm : default);
                    await LookTopRevealImpl(ctx, count, max, match, restTo);
                    break;
                }
            // 查看卡组顶 count 张，玩家按点击顺序重排，再整体放回卡组顶或底。
            case "LookTopReorder":
                {
                    int count = Math.Min(GetInt(op, "count", 5), me.Deck.Count);
                    if (count <= 0) break;
                    var top = me.Deck.Take(count).ToList();
                    var extra = new Dictionary<string, object?>
                    {
                        ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                    };
                    var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReorder",
                        $"按希望的顺序依次选择卡组最上方的 {count} 张卡牌",
                        top.Select(c => c.Id.ToString()).ToList(), count, count, extra);
                    var order = chosen
                        .Select(id => top.FirstOrDefault(c => c.Id.ToString() == id))
                        .Where(c => c is not null)
                        .Select(c => c!.Id)
                        .Distinct()
                        .ToList();
                    // 防御无效或重复回传：未选牌按原相对顺序补齐，避免区域移动丢牌。
                    order.AddRange(top.Where(c => !order.Contains(c.Id)).Select(c => c.Id));
                    int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                        "将这些卡牌按所选顺序放到哪里？", new[] { "卡组最上方", "卡组最下方" });
                    AtomicOps.ReorderTopK(me, order, toBottom: where == 1);
                    break;
                }
            // 公开卡组顶 count 张（默认1，牌留在原位），若其中存在符合 match 的牌则执行 then，否则执行 else。
            // 对应「公开我方卡组最上方的1张卡牌，该卡牌拥有〈X〉特征的场合，……」类条件公开效果。
            case "RevealTopThen":
                {
                    int count = GetInt(op, "count", 1);
                    int peek = Math.Min(count, me.Deck.Count);
                    if (peek > 0)
                    {
                        var top = me.Deck.Take(peek).ToList();
                        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, top.Select(c => c.Info.Number).ToList());
                        var pred = BuildMatchPredicate(op.TryGetProperty("match", out var rm) ? rm : default);
                        bool hit = top.Any(pred);
                        if (hit && op.TryGetProperty("then", out var rthen)) await RunSteps(rthen, ctx);
                        else if (!hit && op.TryGetProperty("else", out var rels)) await RunSteps(rels, ctx);
                    }
                    break;
                }

            // ── 丢弃我方 N 张手牌（玩家自选）──
            case "DiscardOwnChosen":
                await DiscardOwnHand(ctx, GetInt(op, "n", 1));
                break;

            // ── D 阶段新增（P2-P3，覆盖 OP05-OP14 缺失效果）──────────
            case "PlayFromHandFree":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is null) break;
                    int owner = ctx.OwnerIndex; // 手牌卡 owner 恒为效果控制者，FindOwner 不搜手牌会误判 -1
                    await EnsureRoomForCharacter(ctx, owner, target);
                    await AtomicOps.PlayFromHandFree(s, owner, target);
                    break;
                }
            case "PlayEventFromHandFree":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is null) break;
                    if (ctx.Engine is not null)
                        AtomicOps.PlayEventFromHandFree(s, ctx.OwnerIndex, target, ctx.Prompts);
                    break;
                }
            case "AddPowerPersistent":
                {
                    var target = ResolveTarget(op, "target", ctx);
                    if (target is not null) AtomicOps.AddPowerPersistent(target, GetInt(op, "delta", 0));
                    break;
                }
            case "RestActiveDon":
                {
                    // 将己方 N 张活跃咚转为休息状态（不归还到卡组）
                    int n = GetInt(op, "n", 1);
                    int restCount = 0;
                    foreach (var d in me.CostArea)
                    {
                        if (restCount >= n) break;
                        if (d.State == DonState.Active)
                        {
                            d.State = DonState.Rest;
                            restCount++;
                        }
                    }
                    break;
                }
            case "ActiveOwnDon":
                {
                    // 将己方 N 张休息咚转为活跃状态
                    int n = GetInt(op, "n", 1);
                    int actCount = 0;
                    foreach (var d in me.CostArea)
                    {
                        if (actCount >= n) break;
                        if (d.State == DonState.Rest && d.AttachedToCardId is null)
                        {
                            d.State = DonState.Active;
                            actCount++;
                        }
                    }
                    break;
                }
            case "NoPlayCharacter":
                // 本回合中我方无法登场角色卡牌（回合结束自动清除）
                ctx.State.NoPlayCharacterThisTurn.Add(ctx.OwnerIndex);
                break;
            case "SelfToTrash":
                {
                    // 把自身放置到废弃区（不触发 KO 事件），常用于成本
                    BattleEngine.KOCard(s, ctx.OwnerIndex, ctx.Source);
                    break;
                }
            case "LifeToHand":
                {
                    // 将己方生命区最上方 1 张加入手牌
                    if (ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) break; // ST15-001
                    if (me.LifeArea.Count == 0) break;
                    var top = me.LifeArea[0];
                    me.LifeArea.RemoveAt(0);
                    me.Hand.Add(top);
                    break;
                }
            case "OppLifeToHand":
                {
                    // 将对方生命区最上方 1 张加入对方手牌
                    if (opp.LifeArea.Count == 0) break;
                    var top = opp.LifeArea[0];
                    opp.LifeArea.RemoveAt(0);
                    opp.Hand.Add(top);
                    break;
                }
            case "OppLifeToTrash":
                {
                    if (opp.LifeArea.Count == 0) break;
                    bool optional = op.TryGetProperty("optional", out var oltOpt)
                                    && oltOpt.ValueKind == JsonValueKind.True;
                    if (optional)
                    {
                        var text = op.TryGetProperty("text", out var oltText)
                            ? oltText.GetString()
                            : "是否将对方生命区最上方的1张卡牌放置到废弃区？";
                        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, text ?? "是否将对方生命区最上方的1张卡牌放置到废弃区？"))
                            break;
                    }
                    var top = opp.LifeArea[0];
                    opp.LifeArea.RemoveAt(0);
                    top.IsLifeFaceUp = false;
                    opp.Trash.Add(top);
                    // 最外层 EffectRuntime.Resolve 会按生命区前后卡实例差集统一派发
                    // OnLifeLeaveField，避免各生命移动操作漏派或重复派发。
                    break;
                }

            // ── B 阶段（OP09）新增 op ─────────────────────────────────
            // 从手牌按 filter 选最多 1 张角色免费登场（rest=true 时以休息状态登场）
            case "PlayCharFromHand":
                {
                    var pred = BuildMatchPredicate(op.TryGetProperty("filter", out var f) ? f : default);
                    var cands = me.Hand.Where(c => c.Info.Kind == CardKind.Character).Where(pred).ToList();
                    if (cands.Count == 0) break;
                    bool rest = op.TryGetProperty("rest", out var rv) && rv.ValueKind == JsonValueKind.True;
                    var text = op.TryGetProperty("text", out var tx) ? tx.GetString() ?? "从手牌登场最多1张角色" : "从手牌登场最多1张角色";
                    var extra = new Dictionary<string, object?>
                    {
                        ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                    };
                    var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PlayCharFromHand", text,
                        cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
                    if (chosen.Count > 0)
                    {
                        var picked = cands.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
                        if (picked is not null)
                        {
                            await EnsureRoomForCharacter(ctx, ctx.OwnerIndex, picked);
                            await AtomicOps.PlayFromHandFree(s, ctx.OwnerIndex, picked);
                            if (rest) AtomicOps.RestCard(picked);
                        }
                    }
                    break;
                }
            // 从废弃区按 filter 选最多 1 张角色免费登场（rest=true 时以休息状态登场）
            case "PlayCharFromTrash":
                {
                    var pred = BuildMatchPredicate(op.TryGetProperty("filter", out var f) ? f : default);
                    var cands = me.Trash.Where(c => c.Info.Kind == CardKind.Character).Where(pred).ToList();
                    if (cands.Count == 0) break;
                    bool rest = op.TryGetProperty("rest", out var rv) && rv.ValueKind == JsonValueKind.True;
                    var text = op.TryGetProperty("text", out var tx) ? tx.GetString() ?? "从废弃区登场最多1张角色" : "从废弃区登场最多1张角色";
                    var extra = new Dictionary<string, object?>
                    {
                        ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                    };
                    var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PlayCharFromTrash", text,
                        cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
                    if (chosen.Count > 0)
                    {
                        var picked = cands.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
                        if (picked is not null)
                        {
                            await EnsureRoomForCharacter(ctx, ctx.OwnerIndex, picked);
                            await AtomicOps.PlayFromTrashFree(s, ctx.OwnerIndex, picked, rest);
                        }
                    }
                    break;
                }
            // 从"手牌或废弃区"合并候选按 filter 选最多 1 张角色免费登场，按卡实际所在区走对应登场路径。
            // 对应「将手牌或废弃区中最多1张…角色登场」句式(如 OP14-091)。
            case "PlayCharFromHandOrTrash":
                {
                    var pred = BuildMatchPredicate(op.TryGetProperty("filter", out var f) ? f : default);
                    var handCands = me.Hand.Where(c => c.Info.Kind == CardKind.Character).Where(pred).ToList();
                    var trashCands = me.Trash.Where(c => c.Info.Kind == CardKind.Character).Where(pred).ToList();
                    var cands = handCands.Concat(trashCands).ToList();
                    if (cands.Count == 0) break;
                    bool rest = op.TryGetProperty("rest", out var rv) && rv.ValueKind == JsonValueKind.True;
                    var text = op.TryGetProperty("text", out var tx) ? tx.GetString() ?? "从手牌或废弃区登场最多1张角色" : "从手牌或废弃区登场最多1张角色";
                    var extra = new Dictionary<string, object?>
                    {
                        ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                    };
                    var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PlayCharFromHandOrTrash", text,
                        cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
                    if (chosen.Count > 0)
                    {
                        var picked = cands.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
                        if (picked is not null)
                        {
                            await EnsureRoomForCharacter(ctx, ctx.OwnerIndex, picked);
                            bool fromHand = handCands.Any(c => c.Id == picked.Id);
                            if (fromHand)
                            {
                                await AtomicOps.PlayFromHandFree(s, ctx.OwnerIndex, picked);
                                if (rest) AtomicOps.RestCard(picked);
                            }
                            else
                            {
                                await AtomicOps.PlayFromTrashFree(s, ctx.OwnerIndex, picked, rest);
                            }
                        }
                    }
                    break;
                }
        }
    }

    /// <summary>角色登场前腾位：我方角色区已满 5 张时，让玩家选 1 张己方角色放置到废弃区(满场该弃谁由玩家决定，反馈#138)。
    /// 仅对角色卡、确实满员时触发；非角色/未满不打扰。把引擎原"自动弃最左"改为玩家可选，超时兜底取第一张(=旧行为)。
    /// 仅供 DSL 登场入口调用；脚本直连 AtomicOps.Play*Free 仍走引擎自动弃置(暂留限制)。</summary>
    static async Task EnsureRoomForCharacter(EffectContext ctx, int ownerIdx, CardInstance entering)
    {
        if (ownerIdx < 0 || entering.Info.Kind != CardKind.Character) return;
        var p = ctx.State.Players[ownerIdx];
        if (p.Characters.Count < 5) return;
        var ids = p.Characters.Select(c => c.Id.ToString()).ToList();
        var chosen = await ctx.Prompts.ChooseCards(ownerIdx, "ChooseCharToTrashForRoom",
            "我方角色区已满5张，选择1张放置到废弃区以腾出登场位置", ids, 1, 1);
        var victim = (chosen.Count > 0 ? p.Characters.FirstOrDefault(c => c.Id.ToString() == chosen[0]) : null)
                     ?? p.Characters[0];
        p.Characters.Remove(victim);
        p.Trash.Add(victim);
    }

    /// <summary>丢弃我方 n 张手牌（玩家自选；手牌不足则丢全部）。客户端经 extra.choiceCards 显示卡面</summary>
    static async Task DiscardOwnHand(EffectContext ctx, int n)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int actual = Math.Min(n, me.Hand.Count);
        if (actual <= 0) return;
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardOwnChosen",
            $"丢弃 {actual} 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), actual, actual, extra);
        if (chosen.Count == 0)
        {
            for (int i = 0; i < actual && me.Hand.Count > 0; i++) AtomicOps.DiscardHand(me, me.Hand[0]);
            return;
        }
        foreach (var cid in chosen)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null) AtomicOps.DiscardHand(me, card);
        }
    }

    /// <summary>通用探顶实现</summary>
    static async Task LookTopRevealImpl(EffectContext ctx, int count, int max, Func<CardInstance, bool> match, string restTo)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int peek = Math.Min(count, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();
        var candidates = top.Where(match).ToList();
        if (max > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                // 检索卡规范：choiceCards = 确认到的全部 top（让玩家看全），validChoices = 仅可公开 candidates
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                $"确认卡组顶 {peek} 张，公开最多 {max} 张并加入手牌",
                candidates.Select(c => c.Id.ToString()).ToList(), 0, max, extra);
            var revealedNumbers = new List<string>();
            foreach (var cid in chosen)
            {
                var picked = candidates.FirstOrDefault(c => c.Id.ToString() == cid);
                if (picked is not null) { me.Deck.Remove(picked); me.Hand.Add(picked); revealedNumbers.Add(picked.Info.Number); }
            }
            // 检索规范：公开加入手牌的牌，短暂向双方展示
            ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, revealedNumbers);
        }
        // 废弃区的处理不涉及排序。放回卡组底时，原文要求由玩家决定余卡顺序。
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        if (restTo == "trash") me.Trash.AddRange(rest);
        else if (rest.Count <= 1) me.Deck.AddRange(rest);
        else
        {
            var orderedIds = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReorderToDeckBottom",
                "将剩余卡牌自选顺序放回卡组最下方（先选的牌在较上方）",
                rest.Select(c => c.Id.ToString()).ToList(), rest.Count, rest.Count,
                new Dictionary<string, object?>
                {
                    ["choiceCards"] = rest.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                });
            var ordered = orderedIds
                .Select(id => rest.FirstOrDefault(c => c.Id.ToString() == id))
                .Where(c => c is not null)
                .Cast<CardInstance>()
                .Distinct()
                .ToList();
            // 网络异常或旧客户端提交不完整时，不丢失卡牌；未提交的余卡保持原顺序。
            ordered.AddRange(rest.Where(c => !ordered.Contains(c)));
            me.Deck.AddRange(ordered);
        }
    }

    static bool ColorMatches(CardInstance c, string color)
        => c.Info.ColorList.Contains(color);

    /// <summary>解析 DSL 中的 filter 节点，返回卡牌过滤谓词</summary>
    static Func<CardInstance, bool> BuildCardFilter(
        JsonElement node, GameState? state = null, bool fieldUsesCurrentValues = false)
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
                if (kindStr == "Leader"    && c.Info.Kind != CardKind.Leader)    return false;
            }
            if (node.TryGetProperty("keyword", out var kw))
            {
                var k2 = kw.GetString() ?? "";
                if (!c.Info.HasKeyword(k2)) return false;
            }
            // color: 标准色（红/绿/蓝/紫/黑/黄）
            if (node.TryGetProperty("color", out var col))
            {
                if (!ColorMatches(c, col.GetString() ?? "")) return false;
            }
            // property: 属性（斩/打/特/知/…）。多属性卡形如"斩/打"，按 / 拆分匹配（反馈#225：精确比较漏双属性卡）
            if (node.TryGetProperty("property", out var prop))
            {
                if (!c.Info.Property.Split('/').Contains(prop.GetString() ?? "")) return false;
            }
            // 明确写了“原本”的过滤永远只看卡面值。fieldUsesCurrentValues 仅用于
            // 未标明基准的场上筛选，不能让 originalCostLte/originalPowerLte 被费用、贴咚或光环污染。
            if (node.TryGetProperty("originalCostLte", out var oc) && c.Info.Cost > oc.GetInt32()) return false;
            if (node.TryGetProperty("originalCostGte", out var oc2) && c.Info.Cost < oc2.GetInt32()) return false;
            if (node.TryGetProperty("originalPowerLte", out var pp) && c.Info.Power > pp.GetInt32()) return false;
            if (node.TryGetProperty("originalPowerGte", out var pp2) && c.Info.Power < pp2.GetInt32()) return false;
            // currentPowerLte/Gte: 按"当前力量"判定（卡面「力量X以下」未写"原本的"时的默认口径）。
            // 场上卡走 CurrentPowerOf（含贴咚/回合加成/持续光环），非场上候选回退原本力量。
            if (node.TryGetProperty("currentPowerLte", out var cpl) && CurrentPowerOfCard(c) > cpl.GetInt32()) return false;
            if (node.TryGetProperty("currentPowerGte", out var cpg) && CurrentPowerOfCard(c) < cpg.GetInt32()) return false;
            if (node.TryGetProperty("nameEquals", out var nm) && !c.MatchesName(nm.GetString() ?? "")) return false;
            // excludeName: 排除指定卡名（对应「…以外的角色」，如 OP14-091「Mr.2·冯·克雷以外」排除自身同名）
            if (node.TryGetProperty("excludeName", out var exn) && c.MatchesName(exn.GetString() ?? "")) return false;
            // keywordContains: 特征"包含"指定子串（对应「特征中包含〈X〉」语义，区别于精确匹配的 keyword）
            if (node.TryGetProperty("keywordContains", out var kwc))
            {
                var sub = kwc.GetString() ?? "";
                if (!c.Info.HasKeywordContaining(sub)) return false;
            }
            // hasTrigger: 拥有【触发】效果
            if (node.TryGetProperty("hasTrigger", out var ht) && ht.ValueKind == JsonValueKind.True)
            {
                if (string.IsNullOrEmpty(c.Info.Trigger)) return false;
            }
            // noEnterFieldEffect: 没有【登场时】效果（EffectTags 不含 OnEnterField）。对应「没有〈登场时〉效果的角色」。
            if (node.TryGetProperty("noEnterFieldEffect", out var nef) && nef.ValueKind == JsonValueKind.True)
            {
                if (Array.IndexOf(c.Info.EffectTags, "OnEnterField") >= 0) return false;
            }
            return true;
        };
    }

    /// <summary>该卡的"当前力量"：在场上按 CurrentPowerOf（含贴咚/加成/光环），不在场回退原本力量。
    /// 供 currentPowerLte/Gte 过滤键使用；经 EffectRuntime.CurrentState 取 ambient 对局。</summary>
    static int CurrentPowerOfCard(CardInstance c)
    {
        var st = EffectRuntime.CurrentState;
        if (st is null) return c.Info.Power;
        int side = st.SideOf(c);
        return side >= 0 ? st.CurrentPowerOf(side, c) : c.Info.Power;
    }

    /// <summary>场上卡按当前费用判定；非场上候选回退卡面原本费用。</summary>
    static int CurrentCostOfCard(CardInstance c, GameState state)
    {
        int side = state.SideOf(c);
        return side >= 0 ? state.CurrentCostOf(side, c) : c.Info.Cost;
    }

    /// <summary>
    /// 解析"匹配规格"：支持 anyOf（任一子过滤命中即可，用于"X或Y"）+ excludeName（"某名字以外"）。
    /// 无 anyOf 时把节点本身当单个 filter。
    /// </summary>
    static Func<CardInstance, bool> BuildMatchPredicate(
        JsonElement node, GameState? state = null, bool fieldUsesCurrentValues = false)
    {
        if (node.ValueKind != JsonValueKind.Object) return _ => true;
        Func<CardInstance, bool> basePred;
        if (node.TryGetProperty("anyOf", out var anyOf) && anyOf.ValueKind == JsonValueKind.Array)
        {
            var preds = anyOf.EnumerateArray()
                .Select(item => BuildCardFilter(item, state, fieldUsesCurrentValues)).ToList();
            basePred = c => preds.Any(p => p(c));
        }
        else
        {
            basePred = BuildCardFilter(node, state, fieldUsesCurrentValues);
        }
        if (node.TryGetProperty("excludeName", out var ex))
        {
            var exName = ex.GetString() ?? "";
            var inner = basePred;
            basePred = c => inner(c) && !c.MatchesName(exName);
        }
        return basePred;
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
            if (!ctx.Vars.TryGetValue(name, out var v)) return null;
            // 多选 Choose 存的是 List：单目标消费端取第一个（兼容误配）
            if (v is List<CardInstance> list) return list.FirstOrDefault();
            return v as CardInstance;
        }
        return name switch
        {
            "self"        => ctx.Source,
            "selfLeader"  => ctx.State.Players[ctx.OwnerIndex].Leader,
            "oppLeader"   => ctx.State.Players[1 - ctx.OwnerIndex].Leader,
            _ => null,
        };
    }

    /// <summary>目标解析（多目标版）：$var 为 List 时全量返回；单卡包装成单元素列表。供 AddPower 系 op 支持"最多N张各±X"。</summary>
    static List<CardInstance> ResolveTargets(JsonElement op, string key, EffectContext ctx)
    {
        if (op.TryGetProperty(key, out var t) && t.ValueKind == JsonValueKind.String)
        {
            var name = t.GetString() ?? "";
            if (name.StartsWith("$") && ctx.Vars.TryGetValue(name, out var v) && v is List<CardInstance> list)
                return list;
        }
        var single = ResolveTarget(op, key, ctx);
        return single is null ? new List<CardInstance>() : new List<CardInstance> { single };
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

    static bool IsFieldCandidatePrompt(string promptKind)
        => promptKind is "OpponentCharacter" or "OpponentActiveCharacter" or "OpponentRestingCharacter"
            or "OpponentCharacterWithDon" or "OpponentCharacterWithDonGe2"
            or "OpponentLeaderOrCharacter" or "OwnCharacter" or "OwnLeaderOrCharacter"
            or "AnyCharacter" or "OwnStage" or "OpponentStage" or "AnyStage"
            || promptKind.StartsWith("OpponentCharacterCostLe", StringComparison.Ordinal)
            || promptKind.StartsWith("OwnCharacterCostLe", StringComparison.Ordinal);

    static List<CardInstance> BuildCandidates(
        string promptKind, EffectContext ctx, bool legacyCostUsesOriginal = false)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];
        int OwnFieldCost(CardInstance card) => legacyCostUsesOriginal
            ? card.Info.Cost
            : s.CurrentCostOf(ctx.OwnerIndex, card);
        int OpponentFieldCost(CardInstance card) => legacyCostUsesOriginal
            ? card.Info.Cost
            : s.CurrentCostOf(1 - ctx.OwnerIndex, card);
        return promptKind switch
        {
            "OpponentCharacter"           => opp.Characters.ToList(),
            "OpponentActiveCharacter"     => opp.Characters.Where(c => !c.IsTapped).ToList(),
            "AnyCharacter"                => me.Characters.Concat(opp.Characters).ToList(),
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
            "OpponentStage"               => opp.StageCard is { } osc ? new List<CardInstance> { osc } : new(),
            "AnyStage"                    => new[] { me.StageCard, opp.StageCard }.Where(c => c is not null).Cast<CardInstance>().ToList(),
            "OpponentCharacterCostLe5"    => opp.Characters.Where(c => OpponentFieldCost(c) <= 5).ToList(),
            // C2 看对手私有区域：候选 ID 仍是卡 GUID，但仅向查看方暴露
            "OpponentHand"                => opp.Hand.ToList(),
            "OpponentLifeAll"             => opp.LifeArea.ToList(),
            // D 阶段新增：更丰富的候选范围
            "OpponentCharacterCostLe0"    => opp.Characters.Where(c => OpponentFieldCost(c) <= 0).ToList(),
            "OpponentCharacterCostLe1"    => opp.Characters.Where(c => OpponentFieldCost(c) <= 1).ToList(),
            "OpponentCharacterCostLe2"    => opp.Characters.Where(c => OpponentFieldCost(c) <= 2).ToList(),
            "OpponentCharacterCostLe3"    => opp.Characters.Where(c => OpponentFieldCost(c) <= 3).ToList(),
            "OpponentCharacterCostLe4"    => opp.Characters.Where(c => OpponentFieldCost(c) <= 4).ToList(),
            "OpponentCharacterCostLe6"    => opp.Characters.Where(c => OpponentFieldCost(c) <= 6).ToList(),
            "OpponentCharacterCostLe7"    => opp.Characters.Where(c => OpponentFieldCost(c) <= 7).ToList(),
            "OpponentCharacterCostLe8"    => opp.Characters.Where(c => OpponentFieldCost(c) <= 8).ToList(),
            "OpponentCharacterCostLe9"    => opp.Characters.Where(c => OpponentFieldCost(c) <= 9).ToList(),
            "OwnCharacterCostLe2"         => me.Characters.Where(c => OwnFieldCost(c) <= 2).ToList(),
            "OwnCharacterCostLe3"         => me.Characters.Where(c => OwnFieldCost(c) <= 3).ToList(),
            "OwnCharacterCostLe4"         => me.Characters.Where(c => OwnFieldCost(c) <= 4).ToList(),
            "OwnCharacterCostLe5"         => me.Characters.Where(c => OwnFieldCost(c) <= 5).ToList(),
            "OwnCharacterCostLe6"         => me.Characters.Where(c => OwnFieldCost(c) <= 6).ToList(),
            "OwnTrashCharacterCostLe1"    => me.Trash.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 1).ToList(),
            "OwnTrashCharacterCostLe2"    => me.Trash.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 2).ToList(),
            "OwnTrashCharacterCostLe3"    => me.Trash.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 3).ToList(),
            "OwnTrashCharacterCostLe4"    => me.Trash.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 4).ToList(),
            "OwnTrashCharacterCostLe5"    => me.Trash.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 5).ToList(),
            "OwnTrashCharacterCostLe6"    => me.Trash.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 6).ToList(),
            "OwnTrashCharacterCostLe7"    => me.Trash.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 7).ToList(),
            "OwnTrashCharacterCostLe8"    => me.Trash.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 8).ToList(),
            "OwnHandCostLe3"              => me.Hand.Where(c => c.Info.Cost <= 3).ToList(),
            "OwnHandCostLe4"              => me.Hand.Where(c => c.Info.Cost <= 4).ToList(),
            "OwnHandCostLe5"              => me.Hand.Where(c => c.Info.Cost <= 5).ToList(),
            "OwnHandCostLe6"              => me.Hand.Where(c => c.Info.Cost <= 6).ToList(),
            "OpponentCharacterPowerLe"    => opp.Characters.Where(c => c.Info.Power <= 0).ToList(), // 占位
            _                              => UnknownPromptFallback(promptKind),
        };
    }

    /// <summary>未定义的候选 prompt 兜底：记录告警并返回空候选，绝不静默返回对方角色（避免误从对方场选目标）。</summary>
    static List<CardInstance> UnknownPromptFallback(string promptKind)
    {
        Console.Error.WriteLine($"[DSL] 未定义的候选 prompt: {promptKind}，已返回空候选（请在 BuildCandidates 补充定义）");
        return new List<CardInstance>();
    }
}
