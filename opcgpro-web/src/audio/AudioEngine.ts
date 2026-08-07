import { AUDIO_MANIFEST, PRELOAD_SOUND_IDS } from "./audioManifest";
import type { PlaySoundOptions, SoundId } from "./types";

const GLOBAL_VOICE_LIMIT = 8;
const MAX_LATE_PLAYBACK_MS = 1_200;

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

  public isUnlocked(): boolean {
    return this.unlocked;
  }

  public setMuted(muted: boolean): void {
    this.muted = muted;
    this.applyOutputVolume();
  }

  public setVolume(volume: number): void {
    this.volume = Math.max(0, Math.min(1, volume));
    this.applyOutputVolume();
  }

  public async unlock(): Promise<boolean> {
    const context = this.ensureContext();
    if (!context) return false;

    try {
      if (context.state !== "running") await context.resume();
      this.unlocked = context.state === "running";
      if (this.unlocked) void this.preload(PRELOAD_SOUND_IDS);
      return this.unlocked;
    } catch {
      return false;
    }
  }

  public async preload(ids: readonly SoundId[]): Promise<void> {
    await Promise.allSettled(ids.map((id) => this.loadBuffer(id)));
  }

  public play(id: SoundId, options: PlaySoundOptions = {}): void {
    if (!this.unlocked || this.muted || this.volume <= 0) return;
    if (typeof document !== "undefined" && document.visibilityState === "hidden") return;

    const definition = AUDIO_MANIFEST[id];
    const requestedAt = performance.now();
    const previousAt = this.lastPlayedAt.get(id) ?? Number.NEGATIVE_INFINITY;
    if (requestedAt - previousAt < definition.cooldownMs) return;
    this.lastPlayedAt.set(id, requestedAt);

    const cached = this.buffers.get(id);
    if (cached) {
      this.playBuffer(id, cached, options);
      return;
    }

    void this.loadBuffer(id).then((buffer) => {
      if (!buffer || performance.now() - requestedAt > MAX_LATE_PLAYBACK_MS) return;
      if (!this.unlocked || this.muted || this.volume <= 0) return;
      if (typeof document !== "undefined" && document.visibilityState === "hidden") return;
      this.playBuffer(id, buffer, options);
    });
  }

  public stopAll(): void {
    for (const voice of [...this.activeVoices]) this.stopVoice(voice);
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

  private playBuffer(id: SoundId, buffer: AudioBuffer, options: PlaySoundOptions): void {
    const context = this.context;
    const outputGain = this.outputGain;
    if (!context || !outputGain) return;

    if (context.state === "suspended") void context.resume();

    const definition = AUDIO_MANIFEST[id];
    const sameSoundVoices = [...this.activeVoices].filter((voice) => voice.id === id);
    if (sameSoundVoices.length >= definition.maxVoices) return;

    if (this.activeVoices.size >= GLOBAL_VOICE_LIMIT) {
      const lowestPriorityVoice = [...this.activeVoices].sort((a, b) => a.priority - b.priority)[0];
      if (!lowestPriorityVoice || lowestPriorityVoice.priority >= definition.priority) return;
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
  }

  private warnOnce(id: SoundId, error: unknown): void {
    if (process.env.NODE_ENV === "production" || this.warnedSounds.has(id)) return;
    this.warnedSounds.add(id);
    console.warn(`[音效] 无法加载 ${id}`, error);
  }
}

export const audioEngine = new AudioEngine();
