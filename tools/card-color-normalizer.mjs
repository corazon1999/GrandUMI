const COLOR_OVERRIDES = Object.freeze({
  "OP18-021": "绿/紫",
  "OP18-080": "紫/黑",
  "EB05-010": "绿/黄",
});

export function normalizeCardColor(number, color) {
  return (COLOR_OVERRIDES[number] ?? color ?? "").replace(/[・,，]/g, "/");
}
