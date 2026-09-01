import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("领航对决在碰撞时通过统一音频引擎播放重击音效", async () => {
  const [overlay, hook] = await Promise.all([
    readSource("../src/components/game/LeaderClashOverlay.tsx"),
    readSource("../src/hooks/useAudio.ts"),
  ]);

  assert.match(overlay, /const \{ play \} = useAudio\(\)/);
  assert.match(overlay, /play\("damage", \{ volume: 0\.68 \}\)/);
  assert.match(overlay, /IMPACT_SOUND_DELAY_MS = 1_020/);
  assert.match(overlay, /REDUCED_MOTION_IMPACT_SOUND_DELAY_MS = 120/);
  assert.match(hook, /return audioEngine\.play\(id, options\)/);
});

test("领航对决只在音频已解锁且碰撞仍有效时播放，并在卸载时停止自身声音", async () => {
  const [overlay, engine, types] = await Promise.all([
    readSource("../src/components/game/LeaderClashOverlay.tsx"),
    readSource("../src/audio/AudioEngine.ts"),
    readSource("../src/audio/types.ts"),
  ]);

  assert.match(overlay, /useAudioStore\(\(state\) => state\.isUnlocked\)/);
  assert.match(overlay, /remainingDelay < -IMPACT_SOUND_GRACE_MS/);
  assert.match(overlay, /window\.clearTimeout\(timer\);\s*stopSound\(\)/);
  assert.doesNotMatch(overlay, /stopAll\(/);
  assert.match(engine, /public play\(id: SoundId, options: PlaySoundOptions = \{\}\): StopSound/);
  assert.match(engine, /if \(cancelled\) return/);
  assert.match(engine, /this\.activeVoices\.has\(voice\)/);
  assert.match(types, /export type StopSound = \(\) => void/);
});
