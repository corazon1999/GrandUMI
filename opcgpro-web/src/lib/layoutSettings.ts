export type LayoutPreviewMode = "desktop" | "mobile-landscape" | "mobile-portrait";
export type SelectableLayoutPreviewMode = Exclude<LayoutPreviewMode, "mobile-landscape">;

export const LAYOUT_PREVIEW_STORAGE_KEY = "grandumi_home_layout_preview";

// 手机横屏仅作为对局/回放自动旋转后的内部画布，不再暴露为玩家设置。
export const LAYOUT_CANVAS_SIZES: Record<
  LayoutPreviewMode,
  { width?: number; height?: number }
> = {
  desktop: {},
  "mobile-landscape": { width: 844, height: 390 },
  "mobile-portrait": { width: 390, height: 844 },
};

export const LAYOUT_PREVIEW_OPTIONS: Array<{
  value: SelectableLayoutPreviewMode;
  label: string;
  description: string;
  width?: number;
  height?: number;
}> = [
  {
    value: "desktop",
    label: "电脑",
    description: "占满当前浏览器窗口",
  },
  {
    value: "mobile-portrait",
    label: "手机竖屏",
    description: "大厅竖屏；对局自动旋转横屏",
    ...LAYOUT_CANVAS_SIZES["mobile-portrait"],
  },
];

export function isSelectableLayoutPreviewMode(
  value: string | null,
): value is SelectableLayoutPreviewMode {
  return LAYOUT_PREVIEW_OPTIONS.some((option) => option.value === value);
}

export function normalizeStoredLayoutPreviewMode(
  value: string | null,
): SelectableLayoutPreviewMode {
  return isSelectableLayoutPreviewMode(value) ? value : "desktop";
}
