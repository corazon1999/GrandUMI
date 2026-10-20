import { AUDIO_MANIFEST, PRELOAD_SOUND_IDS } from "./audioManifest";
import type { PlaySoundOptions, SoundId, StopSound } from "./types";

const GLOBAL_VOICE_LIMIT = 8;
const MAX_LATE_PLAYBACK_MS = 1_200;
const IDLE_SUSPEND_MS = 1_000;

interface ActiveVoice {
  id: SoundId;
  priority: number;
  source: AudioBufferSourceNode;
  gain: GainNode;
}

class AudioEngine {
  private context: AudioContext | null = null;
  private outputGain: GainNode | null = null;
  private buffers = new Map<SoundId, AudioBuffer>();
  private loading = new Map<SoundId, Promise<AudioBuffer | null>>();
  private activeVoices = new Set<ActiveVoice>();
  private lastPlayedAt = new Map<SoundId, number>();
  private warnedSounds = new Set<SoundId>();
  private unlocked = false;
  private muted = false;
  private volume = 0.7;
  private idleTimer: ReturnType<typeof setTimeout> | null = null;
  private resuming: Promise<boolean> | null = null;
  private suspending: Promise<void> | null = null;
  private desiredRunning = false;
  private playbackEpoch = 0;

  public isUnlocked(): boolean {
    return this.unlocked;
  }

  public setMuted(muted: boolean): void {
    this.muted = muted;
    this.applyOutputVolume();
    if (muted) {
      this.stopAll();
      this.suspendContext();
    }
  }

  public setVolume(volume: number): void {
    this.volume = Math.max(0, Math.min(1, volume));
    this.applyOutputVolume();
    if (this.volume <= 0) {
      this.stopAll();
      this.suspendContext();
    }
  }

  public async unlock(): Promise<boolean> {
    // 解锁表示已经获得用户手势许可，挂起后仍保留此许可，不因页面回到前台而常驻唤醒。
    if (this.unlocked) return true;
    this.unlocked = await this.ensureRunning();
    if (this.unlocked && !this.muted && this.volume > 0) void this.preload(PRELOAD_SOUND_IDS);
    this.scheduleIdleSuspend();
    return this.unlocked;
  }

  public async preload(ids: readonly SoundId[]): Promise<void> {
    await Promise.allSettled(ids.map((id) => this.loadBuffer(id)));
  }

  public play(id: SoundId, options: PlaySoundOptions = {}): StopSound {
    let cancelled = false;
    let voice: ActiveVoice | null = null;
    const epoch = this.playbackEpoch;
    const stop = () => {
      cancelled = true;
      if (voice && this.activeVoices.has(voice)) this.stopVoice(voice);
      voice = null;
      this.scheduleIdleSuspend();
    };

    if (!this.unlocked || this.muted || this.volume <= 0) return stop;
    if (!this.canPlayForCurrentVisibility(options)) return stop;

    const definition = AUDIO_MANIFEST[id];
    const requestedAt = performance.now();
    const previousAt = this.lastPlayedAt.get(id) ?? Number.NEGATIVE_INFINITY;
    if (requestedAt - previousAt < definition.cooldownMs) return stop;
    this.lastPlayedAt.set(id, requestedAt);

    const valid = () => !cancelled && epoch === this.playbackEpoch
      && !this.muted && this.volume > 0
      && performance.now() - requestedAt <= MAX_LATE_PLAYBACK_MS
      && this.canPlayForCurrentVisibility(options);
    const start = async () => {
      try {
        const buffer = this.buffers.get(id) ?? await this.loadBuffer(id);
        if (cancelled) return;
        if (!buffer || !valid()) return;
        if (!await this.ensureRunning() || !valid()) return;
        voice = this.playBuffer(id, buffer, options);
      } catch (error) {
        this.warnOnce(id, error);
      } finally {
        this.scheduleIdleSuspend();
      }
    };
    void start();
    return stop;
  }

  public stopAll(): void {
    this.playbackEpoch += 1;
    for (const voice of [...this.activeVoices]) this.stopVoice(voice);
    this.scheduleIdleSuspend();
  }

  public suspendForBackground(): void {
    this.stopAll();
    this.suspendContext();
  }

  private clearIdleTimer(): void {
    if (this.idleTimer !== null) clearTimeout(this.idleTimer);
    this.idleTimer = null;
  }

  private scheduleIdleSuspend(): void {
    if (!this.context || this.activeVoices.size > 0) return;
    if (this.muted || this.volume <= 0 || (typeof document !== "undefined" && document.visibilityState === "hidden")) {
      this.suspendContext();
      return;
    }
    if (this.idleTimer !== null) return;
    this.idleTimer = setTimeout(() => {
      this.idleTimer = null;
      if (this.activeVoices.size === 0) this.suspendContext();
    }, IDLE_SUSPEND_MS);
  }

  private suspendContext(): void {
    this.clearIdleTimer();
    this.desiredRunning = false;
    const context = this.context;
    if (!context || context.state === "closed" || this.suspending) return;
    if (context.state === "suspended" && !this.resuming) return;
    this.suspending = context.suspend().catch(() => {}).finally(() => { this.suspending = null; });
  }

  private async ensureRunning(): Promise<boolean> {
    this.clearIdleTimer();
    this.desiredRunning = true;
    const context = this.ensureContext();
    if (!context || context.state === "closed") return false;
    if (!this.resuming) {
      this.resuming = (async () => {
        await this.suspending;
        if (!this.desiredRunning) return false;
        if (context.state !== "running") {
          let timeout: ReturnType<typeof setTimeout> | undefined;
          const resumed = context.resume();
          // 部分移动浏览器会延迟兑现恢复请求；静音、后台或过期后不得被迟到恢复重新唤醒。
          void resumed.then(() => { if (!this.desiredRunning) this.suspendContext(); }, () => {});
          try {
            await Promise.race([
              resumed,
              new Promise<never>((_, reject) => {
                timeout = setTimeout(() => {
                  this.suspendContext();
                  reject(new Error("音频恢复超时，已丢弃过期提示"));
                }, MAX_LATE_PLAYBACK_MS);
              }),
            ]);
          } finally { clearTimeout(timeout); }
        }
        return this.desiredRunning && context.state === "running";
      })().catch(() => false).finally(() => { this.resuming = null; });
    }
    return this.resuming;
  }

  private ensureContext(): AudioContext | null {
    if (this.context) return this.context;
    if (typeof window === "undefined") return null;

    const AudioContextClass =
      window.AudioContext ??
      (window as typeof window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
    if (!AudioContextClass) return null;

    this.context = new AudioContextClass();
    this.outputGain = this.context.createGain();
    this.outputGain.connect(this.context.destination);
    this.applyOutputVolume();
    return this.context;
  }

  private applyOutputVolume(): void {
    if (!this.context || !this.outputGain) return;
    const value = this.muted ? 0 : this.volume;
    this.outputGain.gain.setTargetAtTime(value, this.context.currentTime, 0.015);
  }

  private async loadBuffer(id: SoundId): Promise<AudioBuffer | null> {
    const cached = this.buffers.get(id);
    if (cached) return cached;

    const pending = this.loading.get(id);
    if (pending) return pending;

    const promise = this.fetchAndDecode(id);
    this.loading.set(id, promise);
    try {
      return await promise;
    } finally {
      this.loading.delete(id);
    }
  }

  private async fetchAndDecode(id: SoundId): Promise<AudioBuffer | null> {
    const context = this.ensureContext();
    if (!context) return null;

    try {
      const response = await fetch(AUDIO_MANIFEST[id].src, { cache: "force-cache" });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const buffer = await context.decodeAudioData(await response.arrayBuffer());
      this.buffers.set(id, buffer);
      return buffer;
    } catch (error) {
      this.warnOnce(id, error);
      return null;
    }
  }

  private playBuffer(id: SoundId, buffer: AudioBuffer, options: PlaySoundOptions): ActiveVoice | null {
    const context = this.context;
    const outputGain = this.outputGain;
    if (!context || !outputGain) return null;
    if (!this.canPlayForCurrentVisibility(options)) return null;

    if (context.state !== "running") return null;

    const definition = AUDIO_MANIFEST[id];
    const sameSoundVoices = [...this.activeVoices].filter((voice) => voice.id === id);
    if (sameSoundVoices.length >= definition.maxVoices) return null;

    if (this.activeVoices.size >= GLOBAL_VOICE_LIMIT) {
      const lowestPriorityVoice = [...this.activeVoices].sort((a, b) => a.priority - b.priority)[0];
      if (!lowestPriorityVoice || lowestPriorityVoice.priority >= definition.priority) return null;
      this.stopVoice(lowestPriorityVoice);
    }

    const source = context.createBufferSource();
    const gain = context.createGain();
    const requestedVolume = options.volume ?? 1;
    gain.gain.value = Math.max(0, Math.min(1, requestedVolume)) * definition.volume;
    source.buffer = buffer;
    if (definition.pitchVariance) {
      const offset = (Math.random() * 2 - 1) * definition.pitchVariance;
      source.playbackRate.value = 1 + offset;
    }
    source.connect(gain);
    gain.connect(outputGain);

    const voice: ActiveVoice = { id, priority: definition.priority, source, gain };
    this.activeVoices.add(voice);
    source.onended = () => this.disposeVoice(voice);
    source.start();
    return voice;
  }

  private canPlayForCurrentVisibility(options: PlaySoundOptions): boolean {
    if (typeof document === "undefined" || document.visibilityState !== "hidden") return true;
    // 只有明确允许的提示可以短暂唤醒后台音频；恢复太迟会由播放时限丢弃。
    return options.allowWhenHidden === true;
  }

  private stopVoice(voice: ActiveVoice): void {
    this.activeVoices.delete(voice);
    try {
      voice.source.stop();
    } catch {
      // 已自然结束的音源无需再次停止。
    }
    this.disposeVoice(voice);
  }

  private disposeVoice(voice: ActiveVoice): void {
    this.activeVoices.delete(voice);
    voice.source.onended = null;
    voice.source.disconnect();
    voice.gain.disconnect();
    this.scheduleIdleSuspend();
  }

  private warnOnce(id: SoundId, error: unknown): void {
    if (process.env.NODE_ENV === "production" || this.warnedSounds.has(id)) return;
    this.warnedSounds.add(id);
    console.warn(`[音效] 无法加载 ${id}`, error);
  }
}

export const audioEngine = new AudioEngine();
