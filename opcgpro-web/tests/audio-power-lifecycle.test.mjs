import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

const source = readFileSync(new URL("../src/audio/AudioEngine.ts", import.meta.url), "utf8");
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 } }).outputText;

function harness() {
  let now = 0, timerId = 0;
  const timers = new Map();
  const contexts = [], voices = [];
  const document = { visibilityState: "visible" };
  const flush = async () => { for (let i = 0; i < 30; i++) await Promise.resolve(); };
  class Context {
    state = "suspended";
    currentTime = 0;
    destination = {};
    delayedResume = false;
    finishResume = null;
    constructor() { contexts.push(this); }
    resume() {
      if (this.delayedResume) return new Promise(resolve => {
        this.finishResume = () => { this.state = "running"; resolve(); };
      });
      this.state = "running";
      return Promise.resolve();
    }
    suspend() { this.state = "suspended"; return Promise.resolve(); }
    createGain() { return { gain: { value: 1, setTargetAtTime() {} }, connect() {}, disconnect() {} }; }
    decodeAudioData() { return Promise.resolve({ duration: 0.1 }); }
    createBufferSource() {
      const voice = { playbackRate: { value: 1 }, connect() {}, disconnect() {},
        started: false, stopped: false, onended: null,
        start() { this.started = true; }, stop() { this.stopped = true; },
        end() { this.onended?.(); },
      };
      voices.push(voice);
      return voice;
    }
  }
  const definition = { src: "/sound.ogg", volume: 1, maxVoices: 8, cooldownMs: 0, priority: 1 };
  const exports = {};
  vm.runInNewContext(compiled, {
    exports, require: () => ({ AUDIO_MANIFEST: { effect: definition, matchStart: definition }, PRELOAD_SOUND_IDS: ["effect", "matchStart"] }),
    window: { AudioContext: Context }, document,
    process: { env: { NODE_ENV: "production" } }, console,
    performance: { now: () => now },
    fetch: async () => ({ ok: true, arrayBuffer: async () => new ArrayBuffer(0) }),
    setTimeout: (callback, delay) => { const id = ++timerId; timers.set(id, { callback, at: now + delay }); return id; },
    clearTimeout: id => timers.delete(id),
  });
  const advance = async (ms) => {
    const end = now + ms;
    while (true) {
      const next = [...timers].sort((a, b) => a[1].at - b[1].at).find(([, t]) => t.at <= end);
      if (!next) break;
      now = next[1].at; timers.delete(next[0]); next[1].callback(); await flush();
    }
    now = end; await flush();
  };
  return { engine: exports.audioEngine, contexts, voices, document, flush, advance };
}

test("解锁后无声自动挂起，保留许可且下一次声音可恢复", async () => {
  const h = harness();
  assert.equal(await h.engine.unlock(), true);
  await h.flush();
  await h.advance(1000);
  assert.equal(h.contexts[0].state, "suspended");
  assert.equal(h.engine.isUnlocked(), true);
  h.engine.play("effect"); await h.flush();
  assert.equal(h.contexts[0].state, "running");
  assert.equal(h.voices[0].started, true);
  h.voices[0].end(); await h.advance(1000);
  assert.equal(h.contexts[0].state, "suspended");
});

test("静音和零音量立即停止当前音源；取消静音不会无声唤醒", async () => {
  for (const zeroVolume of [false, true]) {
    const h = harness(); await h.engine.unlock(); await h.flush();
    h.engine.play("effect"); await h.flush();
    if (zeroVolume) h.engine.setVolume(0); else h.engine.setMuted(true);
    await h.flush();
    assert.equal(h.voices[0].stopped, true);
    assert.equal(h.contexts[0].state, "suspended");
    if (zeroVolume) h.engine.setVolume(0.7); else h.engine.setMuted(false);
    await h.flush();
    assert.equal(h.contexts[0].state, "suspended");
    h.engine.play("effect"); await h.flush();
    assert.equal(h.voices[1].started, true);
  }
});

test("后台挂起普通声音；明确允许的匹配提示按需恢复并再次挂起", async () => {
  const h = harness(); await h.engine.unlock(); await h.flush();
  h.document.visibilityState = "hidden"; h.engine.suspendForBackground(); await h.flush();
  h.engine.play("effect"); await h.flush();
  assert.equal(h.voices.length, 0);
  h.engine.play("matchStart", { allowWhenHidden: true }); await h.flush();
  assert.equal(h.voices[0].started, true);
  h.voices[0].end(); await h.flush();
  assert.equal(h.contexts[0].state, "suspended");
  h.document.visibilityState = "visible";
  assert.equal(h.contexts[0].state, "suspended");
});

test("浏览器延迟恢复时，静音和取消请求不会产生迟到播放", async () => {
  for (const muted of [false, true]) {
    const h = harness(); await h.engine.unlock(); await h.flush(); await h.advance(1000);
    const context = h.contexts[0]; context.delayedResume = true;
    const stop = h.engine.play("effect"); await h.flush();
    if (muted) { h.engine.setMuted(true); h.engine.setMuted(false); } else stop();
    context.finishResume(); await h.flush(); await h.advance(1000);
    assert.equal(h.voices.length, 0);
    assert.equal(context.state, "suspended");
  }
});

test("恢复超时的后台提示不会在返回前台后补播或让音频常驻运行", async () => {
  const h = harness(); await h.engine.unlock(); await h.flush();
  h.document.visibilityState = "hidden"; h.engine.suspendForBackground(); await h.flush();
  const context = h.contexts[0]; context.delayedResume = true;
  h.engine.play("matchStart", { allowWhenHidden: true }); await h.flush();
  await h.advance(1300);
  h.document.visibilityState = "visible"; context.finishResume(); await h.flush();
  assert.equal(h.voices.length, 0);
  assert.equal(context.state, "suspended");
});

test("一个声音结束时，仍在播放的其他声音不会被空闲计时器打断", async () => {
  const h = harness(); await h.engine.unlock(); await h.flush();
  h.engine.play("effect"); h.engine.play("matchStart"); await h.flush();
  h.voices[0].end(); await h.advance(1500);
  assert.equal(h.contexts[0].state, "running");
  h.voices[1].end(); await h.advance(1000);
  assert.equal(h.contexts[0].state, "suspended");
});
