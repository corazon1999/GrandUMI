#!/usr/bin/env node
import fs from "node:fs";

const file = process.argv[2];
if (!file) {
  console.error("Usage: node tools/verify-matchlog.mjs <matchlog.v1.jsonl>");
  process.exit(2);
}

const text = fs.readFileSync(file, "utf8").trim();
if (!text) fail("matchlog is empty");

const entries = text.split(/\r?\n/).map((line, index) => {
  try {
    return JSON.parse(line);
  } catch (error) {
    fail(`line ${index + 1} is not valid JSON: ${error.message}`);
  }
});

assert(entries.every(e => e.schema === "grandumi.matchlog.v1"), "every entry must use grandumi.matchlog.v1");
assertStrictlyIncreasing(entries.map(e => e.seq), "seq");

const byKind = new Map();
for (const entry of entries) {
  if (!byKind.has(entry.kind)) byKind.set(entry.kind, []);
  byKind.get(entry.kind).push(entry);
}

requireKind("match_start");
requireKind("public_snapshot");
requireKind("private_snapshot");
requireKind("random_event");

const matchStart = byKind.get("match_start")[0];
assert(Number.isInteger(matchStart.payload?.rngSeed), "match_start.payload.rngSeed must be an integer");

const randomEvents = byKind.get("random_event");
assert(randomEvents.length >= 2, "expected at least two random_event entries for initial deck shuffles");
for (const entry of randomEvents) {
  const p = entry.payload ?? {};
  assert(p.type === "shuffle", "random_event.type must be shuffle");
  assert(p.zone === "deck", "random_event.zone must be deck");
  assert(Number.isInteger(p.randomSeq), "random_event.randomSeq must be an integer");
  assert(Number.isInteger(p.rngSeed), "random_event.rngSeed must be an integer");
  assert(Array.isArray(p.beforeOrder), "random_event.beforeOrder must be an array");
  assert(Array.isArray(p.afterOrder), "random_event.afterOrder must be an array");
  assert(p.beforeOrder.length === p.afterOrder.length, "shuffle beforeOrder/afterOrder length mismatch");
  assert(p.afterOrder.every(c => typeof c.id === "string" && typeof c.number === "string"), "afterOrder cards need id and number");
}
assertStrictlyIncreasing(randomEvents.map(e => e.payload.randomSeq), "randomSeq");

const privateSnapshots = byKind.get("private_snapshot");
const latestPrivate = privateSnapshots.at(-1).payload;
assert(Array.isArray(latestPrivate?.players) && latestPrivate.players.length === 2, "private_snapshot.players must contain two players");
for (const [idx, player] of latestPrivate.players.entries()) {
  assert(Array.isArray(player.deck), `private_snapshot.players[${idx}].deck must be an array`);
  assert(Array.isArray(player.life), `private_snapshot.players[${idx}].life must be an array`);
  assert(player.deck.every(cardOk), `private_snapshot.players[${idx}].deck cards need id and number`);
  assert(player.life.every(cardOk), `private_snapshot.players[${idx}].life cards need id and number`);
}

console.log(`OK ${entries.length} entries verified from ${file}`);

function requireKind(kind) {
  assert(byKind.has(kind), `missing ${kind}`);
}

function cardOk(card) {
  return card && typeof card.id === "string" && typeof card.number === "string";
}

function assertStrictlyIncreasing(values, label) {
  for (let i = 0; i < values.length; i++) {
    assert(Number.isInteger(values[i]), `${label}[${i}] must be an integer`);
    if (i > 0) assert(values[i] > values[i - 1], `${label} must be strictly increasing`);
  }
}

function assert(condition, message) {
  if (!condition) fail(message);
}

function fail(message) {
  console.error(`FAIL ${message}`);
  process.exit(1);
}
