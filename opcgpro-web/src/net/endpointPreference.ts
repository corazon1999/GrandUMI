const STORAGE_KEY = "grandumi_ws_endpoint_quality_v1";
const SUCCESS_TTL_MS = 2 * 60 * 1000;
const RTT_TTL_MS = 10 * 60 * 1000;

interface EndpointObservation {
  url: string;
  succeededAt: number;
  rttMs: number | null;
  measuredAt: number;
}

type PreferenceStorage = Pick<Storage, "getItem" | "setItem">;

/** 线路成功记忆只短暂兜底；有双方近期 RTT 时优先实测更快的线路。 */
export class EndpointPreferences {
  private observations = new Map<string, EndpointObservation>();

  constructor(
    private readonly storage: PreferenceStorage | null = browserStorage(),
    private readonly clock: () => number = Date.now,
  ) {
    try {
      const entries: unknown = JSON.parse(this.storage?.getItem(STORAGE_KEY) ?? "[]");
      if (!Array.isArray(entries)) return;
      for (const entry of entries.slice(0, 16)) {
        if (!entry || typeof entry.url !== "string"
            || !Number.isFinite(entry.succeededAt) || !Number.isFinite(entry.measuredAt)
            || (entry.rttMs !== null && !validRtt(entry.rttMs))) continue;
        this.observations.set(entry.url, entry as EndpointObservation);
      }
    } catch {
      // 隐私模式、旧版记忆和损坏缓存均回退到配置顺序。
    }
  }

  recordSuccess(url: string) {
    const previous = this.observations.get(url);
    this.save({ url, succeededAt: this.clock(), rttMs: previous?.rttMs ?? null,
      measuredAt: previous?.measuredAt ?? 0 });
  }

  recordRtt(url: string, rttMs: number) {
    if (!validRtt(rttMs)) return;
    this.save({ url, succeededAt: this.observations.get(url)?.succeededAt ?? 0,
      rttMs, measuredAt: this.clock() });
  }

  rank(endpoints: string[]): string[] {
    if (endpoints.length < 2) return [...endpoints];
    const now = this.clock();
    const measured = endpoints.flatMap(url => {
      const entry = this.observations.get(url);
      return entry?.rttMs !== null && entry?.rttMs !== undefined
        && recent(entry.measuredAt, now, RTT_TTL_MS) ? [entry] : [];
    });
    let preferred = endpoints[0];
    if (measured.some(entry => entry.url === preferred)) {
      preferred = measured.reduce((best, entry) => entry.rttMs! < best.rttMs! ? entry : best).url;
    } else if (measured.length === 0) {
      // 尚未收到心跳时，短暂复用刚成功的线路。旧版无时间戳的永久记忆不再参与排序。
      const succeeded = endpoints.map(url => this.observations.get(url))
        .filter((entry): entry is EndpointObservation => Boolean(entry && recent(entry.succeededAt, now, SUCCESS_TTL_MS)))
        .sort((a, b) => b.succeededAt - a.succeededAt);
      preferred = succeeded[0]?.url ?? preferred;
    }
    // 首选线路没有近期样本时重新尝试它，避免仅凭备用线路的样本一直停留在慢线。
    return [preferred, ...endpoints.filter(url => url !== preferred)];
  }

  private save(entry: EndpointObservation) {
    this.observations.delete(entry.url);
    this.observations.set(entry.url, entry);
    while (this.observations.size > 16) this.observations.delete(this.observations.keys().next().value!);
    try {
      this.storage?.setItem(STORAGE_KEY, JSON.stringify([...this.observations.values()]));
    } catch {
      // 存储不可用时仍使用当前页面内的测量结果。
    }
  }
}

function recent(savedAt: number, now: number, ttlMs: number): boolean {
  return savedAt > 0 && now >= savedAt && now - savedAt < ttlMs;
}

function validRtt(value: number): boolean {
  return Number.isFinite(value) && value >= 0 && value < 10_000;
}

function browserStorage(): PreferenceStorage | null {
  try { return typeof localStorage === "undefined" ? null : localStorage; } catch { return null; }
}
