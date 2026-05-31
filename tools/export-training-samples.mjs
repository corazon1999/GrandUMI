#!/usr/bin/env node

import { readFile, writeFile, mkdir } from "node:fs/promises";
import { dirname, basename, join } from "node:path";

function usage() {
  console.error("Usage: node tools/export-training-samples.mjs <matchlog.jsonl> [output.jsonl]");
}

const input = process.argv[2];
if (!input) {
  usage();
  process.exit(1);
}

const defaultOutput = join(
  dirname(input),
  basename(input).replace(/\.jsonl$/i, ".training.v1.jsonl"),
);
const output = process.argv[3] ?? defaultOutput;

const text = await readFile(input, "utf8");
const entries = text
  .split(/\r?\n/)
  .filter(Boolean)
  .map((line, index) => {
    try {
      return JSON.parse(line);
    } catch (error) {
      throw new Error(`Invalid JSON at line ${index + 1}: ${error.message}`);
    }
  });

const matchStart = entries.find((entry) => entry.kind === "match_start");
const matchEnd = [...entries].reverse().find((entry) => entry.kind === "match_end");
const result = {
  winnerIndex: matchEnd?.payload?.winnerIndex ?? null,
  reason: matchEnd?.payload?.reason ?? "",
};

let lastPrivateSnapshot = null;
const samples = [];

for (const entry of entries) {
  if (entry.kind === "private_snapshot") {
    lastPrivateSnapshot = entry.payload;
    continue;
  }

  if (entry.kind !== "player_action_requested") continue;
  if (typeof entry.actor !== "number" || entry.actor < 0) continue;

  const player = lastPrivateSnapshot?.players?.[entry.actor];
  const opponent = lastPrivateSnapshot?.players?.[1 - entry.actor];

  samples.push({
    schema: "grandumi.training_sample.v1",
    matchId: entry.matchId,
    decisionId: `${entry.matchId}:${entry.seq}`,
    sourceSeq: entry.seq,
    playerIndex: entry.actor,
    turn: entry.turn,
    phase: entry.phase,
    observation: {
      player,
      opponentPublic: opponent
        ? {
            index: opponent.index,
            accountName: opponent.accountName,
            leader: opponent.leader,
            characters: opponent.characters,
            stage: opponent.stage,
            trash: opponent.trash,
            handCount: opponent.hand?.length ?? 0,
            deckCount: opponent.deck?.length ?? 0,
            lifeCount: opponent.life?.length ?? 0,
            costArea: opponent.costArea,
          }
        : null,
      turn: entry.turn,
      phase: entry.phase,
      currentTurnPlayer: lastPrivateSnapshot?.currentTurnPlayer ?? null,
      pendingPrompt: lastPrivateSnapshot?.pendingPrompt ?? null,
      currentBattle: lastPrivateSnapshot?.currentBattle ?? null,
    },
    legalActions: [],
    actionTaken: entry.payload,
    result: {
      ...result,
      isWin: result.winnerIndex === entry.actor,
    },
    metadata: {
      firstPlayer: matchStart?.payload?.firstPlayer ?? null,
      rulesVersion: matchStart?.payload?.rulesVersion ?? "",
      cardDbVersion: matchStart?.payload?.cardDbVersion ?? "",
    },
    hiddenState: lastPrivateSnapshot,
  });
}

await mkdir(dirname(output), { recursive: true });
await writeFile(output, `${samples.map((sample) => JSON.stringify(sample)).join("\n")}\n`, "utf8");
console.log(`Wrote ${samples.length} training samples to ${output}`);
