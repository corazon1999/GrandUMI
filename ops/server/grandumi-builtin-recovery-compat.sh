#!/usr/bin/env bash

# 判断旧内置规则版本到目标版本的单个变更路径是否不影响规则语义。
# 调用方仍必须从受信任的 Git 提交生成变更路径，并在任一路径返回非零时失败关闭。
grandumi_has_exact_blob_transition() {
  local repository="$1"
  local source_commit="$2"
  local target_commit="$3"
  local changed_path="$4"
  local expected_source_blob="$5"
  local expected_target_blob="$6"
  local source_blob target_blob

  source_blob="$(git -C "$repository" rev-parse --verify \
    "$source_commit:$changed_path" 2>/dev/null)" || return 1
  target_blob="$(git -C "$repository" rev-parse --verify \
    "$target_commit:$changed_path" 2>/dev/null)" || return 1
  [[ "$source_blob" == "$expected_source_blob" \
      && "$target_blob" == "$expected_target_blob" ]]
}

grandumi_has_exact_blob_addition() {
  local repository="$1"
  local source_commit="$2"
  local target_commit="$3"
  local changed_path="$4"
  local expected_target_blob="$5"
  local target_blob

  # 新增文件必须在旧规则提交中确实不存在，不能把删除后重加或其他已有内容
  # 误判为本次审计过的纯新增转换。
  if git -C "$repository" cat-file -e "$source_commit:$changed_path" 2>/dev/null; then
    return 1
  fi
  target_blob="$(git -C "$repository" rev-parse --verify \
    "$target_commit:$changed_path" 2>/dev/null)" || return 1
  [[ "$target_blob" == "$expected_target_blob" ]]
}

grandumi_is_builtin_recovery_compatible_change() {
  local repository="$1"
  local source_commit="$2"
  local target_commit="$3"
  local changed_path="$4"

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
      grandumi_has_exact_blob_transition \
        "$repository" "$source_commit" "$target_commit" "$changed_path" \
        52a9dbedc7bd6150e85cb8f50636bc31488f5840 \
        f39ab1998cbfcbb2c2eeea4c30060f48e7b80bb0
      ;;
    服务端WebSocket/QqWhitelistSyncHttpEndpoint.cs)
      # 此转换是独立审计的 QQ 内部 HTTP v1/v2 协议边界；它不参与房间状态、
      # 卡牌规则、动作日志/快照重放，也不改变共享账号权威的激活与 schema。
      grandumi_has_exact_blob_transition \
        "$repository" "$source_commit" "$target_commit" "$changed_path" \
        46511a0350b79a99652ac4d14ea7102c2efbfee4 \
        bbc934908197dc86538f1f47586e3a83bc85d038
      ;;
    服务端WebSocket/Game/AccountRoomOccupancy.cs)
      # 该精确新增 blob 只包含账号占用分类、面向玩家的状态/提示映射和异常载体；
      # 不引用引擎、卡牌、动作日志、快照或恢复存储。旧规则提交中必须没有该路径，
      # 同一路径任何后续内容变化都会因 blob 不匹配而失败关闭。
      grandumi_has_exact_blob_addition \
        "$repository" "$source_commit" "$target_commit" "$changed_path" \
        43af6b1c07a8a9ef5fb33c9805436877ca4a62ef
      ;;
    服务端WebSocket/WebSocketBridge.cs)
      # 与上述结构配套的精确桥接层转换只在新建对局入口读取账号占用并映射错误，
      # 不进入旧房间重放、引擎状态、卡牌规则或持久化路径。
      grandumi_has_exact_blob_transition \
        "$repository" "$source_commit" "$target_commit" "$changed_path" \
        d85b8dfbc7b231db8ed5450a9350c76fcf902292 \
        90cbd522721a7a38ae629f2b586c1a0c11275fb3
      ;;
    *)
      return 1
      ;;
  esac
}
