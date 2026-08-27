using System.Text.Json;
using GrandUMI.Cards;

namespace GrandUMI.Game.Snapshot;

/// <summary>等待随下一份状态快照下发的操作日志事件。</summary>
public sealed record ActionLogEvent(string Action, object? Payload);

/// <summary>
/// 把一次动作广播(lastAction + payload)格式化为「观看者视角」的一行中文操作日志。
/// 返回空字符串表示该动作不记录到操作日志。
/// </summary>
public static class ActionLogFormatter
{
    public static string Format(GameState state, int viewerIndex, string action, JsonElement payload)
    {
        // viewerIndex < 0 为观战视角；操作日志在观战端不展示，这里给中性称呼即可
        string Side(int actor) =>
            viewerIndex < 0 ? $"玩家{actor + 1}" : (actor == viewerIndex ? "我方" : "对手");

        string Card(string number)
        {
            if (string.IsNullOrWhiteSpace(number)) return "未知卡牌";
            var name = CardDatabase.Get(number)?.Name;
            return string.IsNullOrWhiteSpace(name) ? number : $"{number} {name}";
        }

        // 防守方 = 非当前回合方（用于"不阻挡 / 使用反击"这类没有 player 字段的动作）
        int defenderIdx = 1 - state.CurrentTurnPlayer;

        switch (action)
        {
            case "FirstPlayerChosen":
            {
                var chooser = GetInt(payload, "player");
                var goFirst = GetBool(payload, "goFirst");
                return $"[开局] {Side(chooser)}选择了{(goFirst ? "先手" : "后手")}";
            }

            case "PlayCard":
                if (GetBool(payload, "suppressLog")) return "";
                return $"[出牌] {Side(GetInt(payload, "player"))}打出【{Card(GetStr(payload, "cardNumber"))}】";

            case "AttachDon":
            {
                int player = GetInt(payload, "player");
                int count = GetInt(payload, "count", 1);
                string targetId = GetStr(payload, "targetId");
                string targetName = targetId == "leader"
                    ? "领袖"
                    : (FindCard(state, targetId) is { } t ? t.Card.Info.Name : "角色");
                return $"[咚] {Side(player)}为【{targetName}】附加 {count} 个咚";
            }

            case "UndoAttachDon":
            {
                int player = GetInt(payload, "player");
                int count = GetInt(payload, "count", 1);
                string targetId = GetStr(payload, "targetId");
                string targetName = targetId == "leader"
                    ? "领袖"
                    : (FindCard(state, targetId) is { } target ? target.Card.Info.Name : "角色");
                return $"[咚] {Side(player)}撤回了为【{targetName}】附加的 {count} 个咚";
            }

            case "Attack":
            {
                string attackerId = GetStr(payload, "attacker");
                bool targetIsLeader = GetBool(payload, "targetIsLeader");
                var atk = FindCard(state, attackerId);
                int attackerOwner = atk?.Owner ?? state.CurrentTurnPlayer;
                string attackerName = atk?.Card.Info.Name ?? "角色";
                string attackerPower = atk is { } attackerRef
                    ? state.CurrentPowerOf(attackerRef.Owner, attackerRef.Card).ToString()
                    : "?";

                int targetOwner = 1 - attackerOwner;
                CardRef? target = targetIsLeader
                    ? new CardRef(targetOwner, state.Players[targetOwner].Leader)
                    : FindCard(state, GetStr(payload, "targetId"));
                targetOwner = target?.Owner ?? targetOwner;
                string targetName = target?.Card.Info.Name ?? (targetIsLeader ? "领袖" : "角色");
                string targetPower = target is { } targetRef
                    ? state.CurrentPowerOf(targetRef.Owner, targetRef.Card).ToString()
                    : "?";

                return $"[攻击] {Side(attackerOwner)}【{attackerName}】{attackerPower} vs {Side(targetOwner)}【{targetName}】{targetPower}";
            }

            case "DeclareBlocker":
            {
                var blk = FindCard(state, GetStr(payload, "blocker"));
                int owner = blk?.Owner ?? defenderIdx;
                string name = blk?.Card.Info.Name ?? "角色";
                return $"[阻挡] {Side(owner)}用【{name}】宣言【阻挡者】";
            }

            case "PassBlock":
                return $"[阻挡] {Side(defenderIdx)}不进行阻挡";

            case "CounterIcon":
                return $"[反击] {Side(defenderIdx)}使用反击 +{GetInt(payload, "value")}";

            case "UseEffect":
            {
                if (GetBool(payload, "suppressLog")) return "";
                var src = FindCard(state, GetStr(payload, "source"));
                int owner = src?.Owner ?? state.CurrentTurnPlayer;
                string name = src is null ? Card(GetStr(payload, "card")) : $"{src.Value.Card.Info.Number} {src.Value.Card.Info.Name}";
                return $"[启动效果] {Side(owner)}发动【{name}】的效果";
            }

            case "PromptResolved":
            {
                int actor = GetInt(payload, "player");
                string sourceNumber = GetStr(payload, "sourceNumber");
                string source = string.IsNullOrWhiteSpace(sourceNumber) ? "当前流程" : $"【{Card(sourceNumber)}】";
                string promptText = GetStr(payload, "text");
                string visibility = GetStr(payload, "detailVisibility");
                var detailViewers = GetIntArray(payload, "detailViewers");
                bool maySeeDetail = visibility != "restricted"
                    || viewerIndex == actor
                    || detailViewers.Contains(viewerIndex);
                if (!maySeeDetail)
                    return $"[效果选择] {Side(actor)}完成了 {source} 的非公开选择";

                var labels = GetStrArray(payload, "labels");
                string result = labels.Count == 0
                    ? "未选择"
                    : string.Join("、", labels.Select(x => $"【{x}】"));
                string prompt = string.IsNullOrWhiteSpace(promptText) ? "" : $"：“{promptText}”";
                return $"[效果选择] {Side(actor)}处理 {source}{prompt} → {result}";
            }

            case "RevealCards":
            {
                int actor = GetInt(payload, "player");
                var cards = GetStrArray(payload, "cardNumbers").Select(Card).ToArray();
                if (cards.Length == 0) return "";
                return $"[公开] {Side(actor)}公开 {string.Join("、", cards.Select(x => $"【{x}】"))}";
            }

            case "EndTurn":
            {
                int newTurnPlayer = GetInt(payload, "newTurnPlayer");
                int turnCount = GetInt(payload, "turnCount");
                return $"—— 第 {turnCount} 回合 · {Side(newTurnPlayer)}回合 ——";
            }

            case "Surrender":
                return $"[结束] {Side(GetInt(payload, "surrendered"))}投降";

            case "DrawRequested":
                return $"[平局申请] {Side(GetInt(payload, "requester"))}请求因 Bug 平局";

            case "DrawRequestRejected":
                return $"[平局申请] {Side(GetInt(payload, "responder"))}拒绝平局申请（第 {GetInt(payload, "rejectionCount")} 次）";

            case "DrawAgreed":
                return "[结束] 双方同意因 Bug 平局";

            // ── GM 调试动作 ──
            case "DebugAddCard":
                return $"[GM] {Side(GetInt(payload, "player"))}将【{Card(GetStr(payload, "cardNumber"))}】加入手牌";
            case "DebugAddLife":
                return $"[GM] {Side(GetInt(payload, "player"))}将【{Card(GetStr(payload, "cardNumber"))}】置于{Side(GetInt(payload, "target"))}生命区顶端";
            case "DebugAddDon":
                return $"[GM] {Side(GetInt(payload, "player"))}增加 {GetInt(payload, "count", 1)} 个咚";
            case "DebugSummon":
                return $"[GM] {Side(GetInt(payload, "player"))}将【{Card(GetStr(payload, "cardNumber"))}】打出到场上";
            case "DebugKoAll":
                return $"[GM] {Side(GetInt(payload, "player"))}KO 了{Side(GetInt(payload, "target"))}全部角色（{GetInt(payload, "count")} 张）";
            case "DebugRestAll":
                return $"[GM] {Side(GetInt(payload, "player"))}横置了{Side(GetInt(payload, "target"))}全部角色（{GetInt(payload, "count")} 张）";
            case "DebugOP17CoverageStarted":
                return $"{Side(GetInt(payload, "player"))}【GM】开始巡检 OP17 {GetStr(payload, "color")}色卡牌";
            case "DebugOP17CoverageResult":
                return $"{Side(GetInt(payload, "player"))}【GM】完成 OP17 {GetStr(payload, "color")}色巡检：{GetInt(payload, "passed")}/{GetInt(payload, "total")} 通过";

            default:
                return ""; // 其余动作不记录到操作日志
        }
    }

    // ── 工具 ──────────────────────────────────────────────────────────────

    private readonly record struct CardRef(int Owner, CardInstance Card);

    private static CardRef? FindCard(GameState state, string? guidStr)
    {
        if (string.IsNullOrEmpty(guidStr) || !Guid.TryParse(guidStr, out var id)) return null;
        for (int idx = 0; idx < 2; idx++)
        {
            var p = state.Players[idx];
            if (p.Leader.Id == id) return new CardRef(idx, p.Leader);
            var c = p.Characters.FirstOrDefault(x => x.Id == id);
            if (c is not null) return new CardRef(idx, c);
            if (p.StageCard is { } st && st.Id == id) return new CardRef(idx, st);
        }
        return null;
    }

    private static string GetStr(JsonElement payload, string prop)
        => payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static int GetInt(JsonElement payload, string prop, int fallback = 0)
        => payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : fallback;

    private static bool GetBool(JsonElement payload, string prop)
        => payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    private static List<string> GetStrArray(JsonElement payload, string prop)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(prop, out var value)
            || value.ValueKind != JsonValueKind.Array)
            return new List<string>();

        return value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? "")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static List<int> GetIntArray(JsonElement payload, string prop)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(prop, out var value)
            || value.ValueKind != JsonValueKind.Array)
            return new List<int>();

        return value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _))
            .Select(x => x.GetInt32())
            .ToList();
    }
}
