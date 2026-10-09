/** 新协议只展示当前佩戴；旧回放没有佩戴字段时兼容显示第一个历史称号。 */
export function displayedSeasonTitle(identity?: {
  seasonTitles?: string[];
  equippedSeasonTitle?: string | null;
} | null): string | null {
  if (!identity) return null;
  if (identity.equippedSeasonTitle === undefined) return identity.seasonTitles?.[0] ?? null;
  return identity.equippedSeasonTitle ?? null;
}

/** 新快照的明确取消优先；旧排位回放仍读取其历史身份。 */
export function snapshotSeasonTitle(player?: {
  equippedSeasonTitle?: string | null;
  rankIdentity?: { seasonTitles?: string[]; equippedSeasonTitle?: string | null } | null;
} | null): string | null | undefined {
  if (player?.equippedSeasonTitle !== undefined) return player.equippedSeasonTitle;
  return player?.rankIdentity ? displayedSeasonTitle(player.rankIdentity) : undefined;
}
