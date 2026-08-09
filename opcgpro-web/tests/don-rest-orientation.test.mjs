import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("resting DON uses a horizontal slot and a quarter-turned card face", async () => {
  const [donArea, donCardItem] = await Promise.all([
    readSource("../src/components/game/DonArea.tsx"),
    readSource("../src/components/game/DonCardItem.tsx"),
  ]);

  assert.match(donArea, /const restSlotSizes = \{[\s\S]*?sm: "h-\[4\.5rem\] w-\[6\.3rem\]"/);
  assert.match(donArea, /state === "rest" \? restSlotSizes\[cardSize\] : slotSizes\[cardSize\]/);
  assert.match(donCardItem, /const restSizeClass = \{[\s\S]*?sm: "w-\[6\.3rem\] h-\[4\.5rem\]"/);
  assert.match(donCardItem, /state === "rest" \? restSizeClass\[size\] : sizeClass\[size\]/);
  assert.match(donCardItem, /state === "rest" && "rotate-90"/);
});
