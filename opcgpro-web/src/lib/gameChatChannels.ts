export type GameChatChannel = "room" | "players" | "spectators";

export function gameChatChannels(tournament: boolean, observer: boolean, grants?: readonly string[] | null): GameChatChannel[] {
  if (!tournament) return ["room"];
  const channels = grants?.filter((channel): channel is "players" | "spectators" => channel === "players" || channel === "spectators");
  return channels?.length ? [...new Set(channels)] : [observer ? "spectators" : "players"];
}

export function gameChatChannelLabel(channel: GameChatChannel) {
  return channel === "players" ? "对战频道" : channel === "spectators" ? "观战频道" : "局内聊天";
}

/** 各频道独立保留最近50条，观战刷屏不会挤掉选手频道的记录。 */
export function appendGameChatMessage<T extends { channel: GameChatChannel }>(previous: readonly T[], message: T): T[] {
  const remaining = 49;
  const retained = previous.filter(item => item.channel !== message.channel).length;
  let sameChannel = previous.length - retained;
  return [...previous.filter(item => item.channel !== message.channel || --sameChannel < remaining), message];
}
