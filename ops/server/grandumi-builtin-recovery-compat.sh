#!/usr/bin/env bash

# 判断旧内置规则版本到目标版本的单个变更路径是否不影响规则语义。
# 调用方仍必须从受信任的 Git 提交生成变更路径，并在任一路径返回非零时失败关闭。
grandumi_is_builtin_recovery_compatible_change() {
  local repository="$1"
  local source_commit="$2"
  local target_commit="$3"
  local changed_path="$4"
  local source_blob target_blob

  case "$changed_path" in
    服务端WebSocket/Program.cs|\
    服务端WebSocket/Effects/Rules/CardRuleset.cs|\
    服务端WebSocket/Game/GameRoomManager.cs|\
    服务端WebSocket/Game/MatchReplay.cs|\
    服务端WebSocket/Game/RoomRecoverySnapshotStore.cs|\
    服务端WebSocket/Game/TerminalOutcomeStore.cs|\
    服务端WebSocket/Persistence/CloudReplayStore.cs)
      return 0
      ;;
    服务端WebSocket/Persistence/QqAccessStore.cs)
      # 此转换已逐 hunk 审计：只增加双群 QQ 白名单快照的校验、幂等与审计，
      # 未改变卡牌规则、对局状态、动作日志/快照恢复或共享账号库激活/schema。
      # 同一路径今后的任意变化都会因 blob 不匹配而失败，不能继承本次授权。
      source_blob="$(git -C "$repository" rev-parse --verify \
        "$source_commit:$changed_path" 2>/dev/null)" || return 1
      target_blob="$(git -C "$repository" rev-parse --verify \
        "$target_commit:$changed_path" 2>/dev/null)" || return 1
      [[ "$source_blob" == 52a9dbedc7bd6150e85cb8f50636bc31488f5840 \
          && "$target_blob" == f39ab1998cbfcbb2c2eeea4c30060f48e7b80bb0 ]]
      ;;
    *)
      return 1
      ;;
  esac
}
