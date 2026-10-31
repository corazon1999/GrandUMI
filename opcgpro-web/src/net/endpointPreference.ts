const STORAGE_KEY = "grandumi_ws_endpoint_quality_v1";
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
    // 主备顺序由运行时线路清单决定。RTT 只用于诊断记录，不能因为 CDN
    // 某一时段更快就把故障备用线路提升为主线路。
    return [...endpoints];
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

function validRtt(value: number): boolean {
  return Number.isFinite(value) && value >= 0 && value < 10_000;
}

function browserStorage(): PreferenceStorage | null {
  try { return typeof localStorage === "undefined" ? null : localStorage; } catch { return null; }
}
