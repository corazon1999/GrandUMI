export function hasValue(value) {
  if (value === undefined || value === null) return false;
  if (typeof value === "string") return value.trim().length > 0;
  if (Array.isArray(value)) return value.some(hasValue);
  return true;
}

/** 清单 URL 的版本参数不参与卡图身份判断。 */
export function canonicalSpritePath(sprite) {
  return typeof sprite === "string" ? sprite.split("?")[0] : "";
}

/**
 * 新导入图片优先；旧清单只补回仍未出现的异画。
 * 旧正画无论是否带版本参数都不得被误判为异画。
 */
export function mergeImageSprites(existingSprites, importedSprites, mainSpritePath) {
  const mainPath = canonicalSpritePath(mainSpritePath);
  const result = [];
  const seen = new Set();
  for (const sprite of [...importedSprites, ...existingSprites]) {
    const canonical = canonicalSpritePath(sprite);
    if (!canonical || seen.has(canonical)) continue;
    if (canonical === mainPath && !importedSprites.includes(sprite)) continue;
    seen.add(canonical);
    result.push(sprite);
  }
  return result;
}

function mergeAliases(existing, incoming) {
  const aliases = new Set([
    ...(Array.isArray(existing.alsoNames) ? existing.alsoNames : []),
    ...(Array.isArray(incoming.alsoNames) ? incoming.alsoNames : []),
  ].filter(Boolean));
  if (hasValue(existing.name) && hasValue(incoming.name) && existing.name !== incoming.name) {
    aliases.add(existing.name);
  }
  aliases.delete(incoming.name);
  return [...aliases];
}

/**
 * 以表格中的非空新值为准，同时保留旧卡、旧扩展字段以及新表中的空缺值。
 * 名称发生变化时把旧名称保留为别名，避免旧卡组搜索和历史记录失去识别能力。
 */
export function mergeCardUpdates(existingCards, importedCards) {
  const merged = new Map(existingCards.map((card) => [card.number, { ...card }]));

  for (const incoming of importedCards) {
    const existing = merged.get(incoming.number);
    if (!existing) {
      merged.set(incoming.number, { ...incoming });
      continue;
    }

    const next = { ...existing };
    for (const [field, value] of Object.entries(incoming)) {
      if (field === "number" || hasValue(value) || !hasValue(existing[field])) {
        next[field] = value;
      }
    }

    const aliases = mergeAliases(existing, next);
    if (aliases.length) next.alsoNames = aliases;
    else delete next.alsoNames;
    merged.set(incoming.number, next);
  }

  return [...merged.values()].sort((a, b) =>
    a.number.localeCompare(b.number, "en", { numeric: true }),
  );
}

export function describeCardChanges(existingCards, mergedCards) {
  const existing = new Map(existingCards.map((card) => [card.number, card]));
  const additions = [];
  const updates = [];
  const preserved = [];

  for (const card of mergedCards) {
    const before = existing.get(card.number);
    if (!before) {
      additions.push(card.number);
      continue;
    }
    const fields = [...new Set([...Object.keys(before), ...Object.keys(card)])]
      .filter((field) => JSON.stringify(before[field]) !== JSON.stringify(card[field]));
    if (fields.length) updates.push({ number: card.number, fields });
    else preserved.push(card.number);
  }

  return { additions, updates, preserved };
}

export function describeCompatibility(existingCards, importedCards) {
  const existing = new Map(existingCards.map((card) => [card.number, card]));
  const blankFieldsPreserved = [];
  const nonEmptyConflicts = [];

  for (const incoming of importedCards) {
    const before = existing.get(incoming.number);
    if (!before) continue;
    for (const [field, value] of Object.entries(incoming)) {
      if (field === "number") continue;
      if (!hasValue(value) && hasValue(before[field])) {
        blankFieldsPreserved.push({ number: incoming.number, field });
      } else if (
        hasValue(value)
        && hasValue(before[field])
        && JSON.stringify(value) !== JSON.stringify(before[field])
      ) {
        nonEmptyConflicts.push({ number: incoming.number, field });
      }
    }
  }

  return { blankFieldsPreserved, nonEmptyConflicts };
}
