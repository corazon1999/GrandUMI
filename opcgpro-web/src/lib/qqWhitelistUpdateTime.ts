export const qqWhitelistUpdateDateFromUnixMilliseconds = (timestampMilliseconds: number) => (
  new Date(timestampMilliseconds)
);

export const formatQqWhitelistUpdateTime = (
  timestampMilliseconds: number,
  timeZone?: string,
) => new Intl.DateTimeFormat("zh-CN", {
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  hour12: false,
  ...(timeZone ? { timeZone } : {}),
}).format(qqWhitelistUpdateDateFromUnixMilliseconds(timestampMilliseconds));
