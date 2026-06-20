using System.Text.Json;
using GrandUMI.Cards;

namespace GrandUMI.Game.Snapshot;

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

        string Name(string number) => CardDatabase.Get(number)?.Name ?? number;

        // 防守方 = 非当前回合方（用于"不阻挡 / 使用反击"这类没有 player 字段的动作）
        int defenderIdx = 1 - state.CurrentTurnPlayer;

        switch (action)
        {
            case "PlayCard":
                return $"{Side(GetInt(payload, "player"))} 打出 {Name(GetStr(payload, "cardNumber"))}";

            case "AttachDon":
            {
                int player = GetInt(payload, "player");
                int count = GetInt(payload, "count", 1);
                string targetId = GetStr(payload, "targetId");
                string targetName = targetId == "leader"
                    ? "领袖"
                    : (FindCard(state, targetId) is { } t ? t.Card.Info.Name : "角色");
                return $"{Side(player)} 为 {targetName} 附加 {count} 个咚";
            }

            case "Attack":
            {
                string attackerId = GetStr(payload, "attacker");
                bool targetIsLeader = GetBool(payload, "targetIsLeader");
                var atk = FindCard(state, attackerId);
                int attackerOwner = atk?.Owner ?? state.CurrentTurnPlayer;
                string attackerName = atk?.Card.Info.Name ?? "角色";
                string targetDesc;
                if (targetIsLeader)
                    targetDesc = $"{Side(1 - attackerOwner)}领袖";
                else
                {
                    var tgt = FindCard(state, GetStr(payload, "targetId"));
                    targetDesc = tgt?.Card.Info.Name ?? "角色";
                }
                return $"{Side(attackerOwner)} 用 {attackerName} 攻击 {targetDesc}";
            }

            case "DeclareBlocker":
            {
                var blk = FindCard(state, GetStr(payload, "blocker"));
                int owner = blk?.Owner ?? defenderIdx;
                string name = blk?.Card.Info.Name ?? "角色";
                return $"{Side(owner)} 用 {name} 宣言【阻挡者】";
            }

            case "PassBlock":
                return $"{Side(defenderIdx)} 不进行阻挡";

            case "CounterIcon":
                return $"{Side(defenderIdx)} 使用反击 +{GetInt(payload, "value")}";

            case "UseEffect":
            {
                var src = FindCard(state, GetStr(payload, "source"));
                int owner = src?.Owner ?? state.CurrentTurnPlayer;
                string name = src?.Card.Info.Name ?? Name(GetStr(payload, "card"));
                return $"{Side(owner)} 发动 {name} 的效果";
            }

            case "EndTurn":
            {
                int newTurnPlayer = GetInt(payload, "newTurnPlayer");
                int turnCount = GetInt(payload, "turnCount");
                return $"—— 第 {turnCount} 回合 · {Side(newTurnPlayer)}回合 ——";
            }

            case "Surrender":
                return $"{Side(GetInt(payload, "surrendered"))} 投降";

            // ── GM 调试动作 ──
            case "DebugAddCard":
                return $"{Side(GetInt(payload, "player"))}【GM】将 {Name(GetStr(payload, "cardNumber"))} 加入手牌";
            case "DebugAddDon":
                return $"{Side(GetInt(payload, "player"))}【GM】增加 {GetInt(payload, "count", 1)} 个咚";
            case "DebugSummon":
                return $"{Side(GetInt(payload, "player"))}【GM】将 {Name(GetStr(payload, "cardNumber"))} 打出到场上";
            case "DebugKoAll":
                return $"{Side(GetInt(payload, "player"))}【GM】KO 了 {Side(GetInt(payload, "target"))}全部角色（{GetInt(payload, "count")} 张）";
            case "DebugRestAll":
                return $"{Side(GetInt(payload, "player"))}【GM】横置了 {Side(GetInt(payload, "target"))}全部角色（{GetInt(payload, "count")} 张）";

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
}
