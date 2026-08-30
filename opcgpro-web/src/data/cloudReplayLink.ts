const CLOUD_REPLAY_LINK_PREFIX = "grandumi_cloud_replay_link:";

export function rememberCloudReplayLink(localReplayId: string, cloudReplayId: string) {
  if (typeof window === "undefined") return;
  sessionStorage.setItem(`${CLOUD_REPLAY_LINK_PREFIX}${localReplayId}`, cloudReplayId);
}

export function readCloudReplayLink(localReplayId: string): string | null {
  if (typeof window === "undefined") return null;
  return sessionStorage.getItem(`${CLOUD_REPLAY_LINK_PREFIX}${localReplayId}`);
}
