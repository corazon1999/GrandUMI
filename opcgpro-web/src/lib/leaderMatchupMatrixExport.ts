import { getCard } from "@/data/CardLoader";
import { CARD_BACK_SRC, thumbSrc } from "@/lib/sprite";
import type {
  LeaderFilterTier,
  LeaderLeaderboardItem,
  LeaderboardPeriod,
  LeaderMatchupItem,
  MsgLeaderMatchupMatrix,
} from "@/types/net";

export const LEADER_MATCHUP_MATRIX_LIMIT = 20;
export const LEADER_MATRIX_EXPORT_CELL_WIDTH = 104;
export const LEADER_MATRIX_EXPORT_ROW_HEIGHT = 82;
export const LEADER_MATRIX_EXPORT_MIN_WIDTH = 1200;

const CANVAS_PADDING = 42;
const HEADER_HEIGHT = 190;
const ROW_HEADER_WIDTH = 242;
const COLUMN_HEADER_HEIGHT = 154;
const FOOTER_HEIGHT = 48;
const FONT_FAMILY = '"Microsoft YaHei", "Noto Sans SC", Arial, sans-serif';

const PERIOD_LABELS: Record<LeaderboardPeriod, string> = {
  "7d": "近 7 天",
  "30d": "近 30 天",
  all: "全部时间",
};

const FILTER_TIER_LABELS: Record<LeaderFilterTier, string> = {
  relaxed: "100 / 300 场",
  standard: "500 / 3000 场",
  all: "全部 Leader",
};

export interface LeaderMatchupMatrixExportOptions {
  period: LeaderboardPeriod;
  filterTier: LeaderFilterTier;
  leaderboardItems: LeaderLeaderboardItem[];
  matrix: MsgLeaderMatchupMatrix;
  totalMatches: number;
  minimumGames: number;
  generatedAt?: Date;
}

export interface GeneratedLeaderMatchupMatrixImage {
  blob: Blob;
  filename: string;
  width: number;
  height: number;
  generatedAtText: string;
}

export interface LeaderMatchupMatrixExportLayout {
  width: number;
  height: number;
  leaderCount: number;
}

export function selectLeaderMatchupMatrixLeaders(
  leaderboardItems: LeaderLeaderboardItem[],
): LeaderLeaderboardItem[] {
  return [...leaderboardItems]
    .filter((item) => item.rank != null)
    .sort((left, right) => right.winRate - left.winRate || (left.rank ?? 0) - (right.rank ?? 0))
    .slice(0, LEADER_MATCHUP_MATRIX_LIMIT);
}

export function getLeaderMatchupMatrixExportLayout(
  leaderCount: number,
): LeaderMatchupMatrixExportLayout {
  const normalizedCount = Math.max(0, Math.min(LEADER_MATCHUP_MATRIX_LIMIT, Math.floor(leaderCount)));
  return {
    width: Math.max(
      LEADER_MATRIX_EXPORT_MIN_WIDTH,
      CANVAS_PADDING * 2 + ROW_HEADER_WIDTH + normalizedCount * LEADER_MATRIX_EXPORT_CELL_WIDTH,
    ),
    height: CANVAS_PADDING * 2 + HEADER_HEIGHT + COLUMN_HEADER_HEIGHT
      + normalizedCount * LEADER_MATRIX_EXPORT_ROW_HEIGHT + FOOTER_HEIGHT,
    leaderCount: normalizedCount,
  };
}

function pad(value: number): string {
  return String(value).padStart(2, "0");
}

export function formatLeaderMatchupMatrixExportTimestamp(date: Date): {
  text: string;
  filename: string;
} {
  const year = date.getFullYear();
  const month = pad(date.getMonth() + 1);
  const day = pad(date.getDate());
  const hours = pad(date.getHours());
  const minutes = pad(date.getMinutes());
  const seconds = pad(date.getSeconds());
  return {
    text: `${year}-${month}-${day} ${hours}:${minutes}:${seconds}`,
    filename: `${year}${month}${day}-${hours}${minutes}${seconds}`,
  };
}

function percent(value: number | null | undefined): string {
  return value == null ? "—" : `${(value * 100).toFixed(1)}%`;
}

function roundedRect(
  context: CanvasRenderingContext2D,
  x: number,
  y: number,
  width: number,
  height: number,
  radius: number,
): void {
  const normalizedRadius = Math.min(radius, width / 2, height / 2);
  context.beginPath();
  context.moveTo(x + normalizedRadius, y);
  context.arcTo(x + width, y, x + width, y + height, normalizedRadius);
  context.arcTo(x + width, y + height, x, y + height, normalizedRadius);
  context.arcTo(x, y + height, x, y, normalizedRadius);
  context.arcTo(x, y, x + width, y, normalizedRadius);
  context.closePath();
}

function latestLeaderSprite(leaderNumber: string): string {
  const card = getCard(leaderNumber);
  if (card?.sprites?.length) return card.sprites[card.sprites.length - 1];
  return card?.sprite ?? CARD_BACK_SRC;
}

function leaderImageCandidates(leaderNumber: string): string[] {
  const card = getCard(leaderNumber);
  const sprite = latestLeaderSprite(leaderNumber);
  return [...new Set([
    thumbSrc(sprite),
    sprite,
    card?.image,
    CARD_BACK_SRC,
  ].filter((source): source is string => Boolean(source)))];
}

function loadImageCandidate(source: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    const absoluteUrl = new URL(source, window.location.href);
    if (absoluteUrl.origin !== window.location.origin) image.crossOrigin = "anonymous";
    image.decoding = "async";
    const timer = window.setTimeout(() => {
      image.src = "";
      reject(new Error(`Leader 卡图加载超时：${source}`));
    }, 10_000);
    image.onload = () => {
      window.clearTimeout(timer);
      resolve(image);
    };
    image.onerror = () => {
      window.clearTimeout(timer);
      reject(new Error(`Leader 卡图加载失败：${source}`));
    };
    image.src = absoluteUrl.href;
  });
}

async function loadLeaderImage(leaderNumber: string): Promise<HTMLImageElement | null> {
  for (const source of leaderImageCandidates(leaderNumber)) {
    try {
      return await loadImageCandidate(source);
    } catch {
      // 单张卡图或跨域回退失败时继续尝试；全部失败后绘制文字占位。
    }
  }
  return null;
}

function drawLeaderImage(
  context: CanvasRenderingContext2D,
  image: HTMLImageElement | null,
  leaderNumber: string,
  x: number,
  y: number,
  width: number,
  height: number,
): void {
  context.save();
  roundedRect(context, x, y, width, height, 6);
  context.clip();
  context.fillStyle = "#080b12";
  context.fillRect(x, y, width, height);
  if (image && image.naturalWidth > 0 && image.naturalHeight > 0) {
    const scale = Math.max(width / image.naturalWidth, height / image.naturalHeight);
    const drawWidth = image.naturalWidth * scale;
    const drawHeight = image.naturalHeight * scale;
    context.drawImage(
      image,
      x + (width - drawWidth) / 2,
      y + (height - drawHeight) / 2,
      drawWidth,
      drawHeight,
    );
  } else {
    context.fillStyle = "#9ca3af";
    context.font = `700 11px ${FONT_FAMILY}`;
    context.textAlign = "center";
    context.textBaseline = "middle";
    context.fillText(leaderNumber, x + width / 2, y + height / 2);
  }
  context.restore();
  context.strokeStyle = "#374151";
  context.lineWidth = 1;
  roundedRect(context, x, y, width, height, 6);
  context.stroke();
}

function cellColors(item: LeaderMatchupItem | undefined): { background: string; foreground: string } {
  if (!item || item.isMirror || item.winRate == null) {
    return { background: "#111827", foreground: "#6b7280" };
  }
  if (item.winRate >= 0.6) return { background: "#073f36", foreground: "#6ee7b7" };
  if (item.winRate > 0.5) return { background: "#123a2c", foreground: "#86efac" };
  if (item.winRate === 0.5) return { background: "#263142", foreground: "#e2e8f0" };
  if (item.winRate >= 0.4) return { background: "#45202d", foreground: "#fda4af" };
  return { background: "#4a121d", foreground: "#fda4af" };
}

function drawMatrixCell(
  context: CanvasRenderingContext2D,
  item: LeaderMatchupItem | undefined,
  x: number,
  y: number,
): void {
  const colors = cellColors(item);
  context.fillStyle = colors.background;
  context.fillRect(x, y, LEADER_MATRIX_EXPORT_CELL_WIDTH, LEADER_MATRIX_EXPORT_ROW_HEIGHT);
  context.strokeStyle = "#263244";
  context.lineWidth = 1;
  context.strokeRect(x + 0.5, y + 0.5, LEADER_MATRIX_EXPORT_CELL_WIDTH - 1, LEADER_MATRIX_EXPORT_ROW_HEIGHT - 1);

  context.fillStyle = colors.foreground;
  context.font = `800 18px ${FONT_FAMILY}`;
  context.textAlign = "center";
  context.textBaseline = "middle";
  context.fillText(item?.isMirror ? "—" : percent(item?.winRate), x + LEADER_MATRIX_EXPORT_CELL_WIDTH / 2, y + 31);

  const lowSample = Boolean(item && !item.isMirror && item.games > 0 && item.games < 5);
  context.fillStyle = lowSample ? "#fbbf24" : "#94a3b8";
  context.font = `600 11px ${FONT_FAMILY}`;
  const sampleText = item?.isMirror
    ? `${item.games} 场镜像`
    : item && item.games > 0
      ? `${item.games} 场${lowSample ? " · 低样本" : ""}`
      : "暂无交手";
  context.fillText(sampleText, x + LEADER_MATRIX_EXPORT_CELL_WIDTH / 2, y + 56);
}

function canvasToBlob(canvas: HTMLCanvasElement): Promise<Blob> {
  return new Promise((resolve, reject) => {
    try {
      canvas.toBlob((blob) => {
        if (blob) resolve(blob);
        else reject(new Error("浏览器未能生成 PNG 图片"));
      }, "image/png");
    } catch (error) {
      reject(error instanceof Error ? error : new Error("卡图跨域限制导致 PNG 生成失败"));
    }
  });
}

export async function renderLeaderMatchupMatrixImage(
  options: LeaderMatchupMatrixExportOptions,
): Promise<HTMLCanvasElement> {
  if (options.matrix.period !== options.period || options.matrix.filterTier !== options.filterTier) {
    throw new Error("对阵矩阵与当前筛选条件不一致，请重新加载后再导出");
  }
  if (options.matrix.result !== true) {
    throw new Error(options.matrix.error ?? "对阵矩阵尚未准备完成");
  }

  const leaders = selectLeaderMatchupMatrixLeaders(options.leaderboardItems);
  if (leaders.length === 0) throw new Error("当前筛选条件下没有可导出的 Leader");
  const layout = getLeaderMatchupMatrixExportLayout(leaders.length);
  const canvas = document.createElement("canvas");
  canvas.width = layout.width;
  canvas.height = layout.height;
  const context = canvas.getContext("2d");
  if (!context) throw new Error("当前浏览器不支持图片导出");

  const generatedAt = options.generatedAt ?? new Date();
  const timestamp = formatLeaderMatchupMatrixExportTimestamp(generatedAt);
  const tableX = CANVAS_PADDING;
  const tableY = CANVAS_PADDING + HEADER_HEIGHT;
  const matrixX = tableX + ROW_HEADER_WIDTH;
  const matrixY = tableY + COLUMN_HEADER_HEIGHT;

  const background = context.createLinearGradient(0, 0, 0, canvas.height);
  background.addColorStop(0, "#080c16");
  background.addColorStop(1, "#03060c");
  context.fillStyle = background;
  context.fillRect(0, 0, canvas.width, canvas.height);
  context.fillStyle = "#f97316";
  context.fillRect(0, 0, canvas.width, 8);

  context.textAlign = "left";
  context.textBaseline = "alphabetic";
  context.fillStyle = "#ffffff";
  context.font = `800 38px ${FONT_FAMILY}`;
  context.fillText(`Leader 对阵一图流 · 榜前 ${leaders.length}`, CANVAS_PADDING, CANVAS_PADDING + 50);
  context.fillStyle = "#cbd5e1";
  context.font = `600 18px ${FONT_FAMILY}`;
  context.fillText(
    `统计周期：${PERIOD_LABELS[options.period]}　筛选档位：${FILTER_TIER_LABELS[options.filterTier]}`,
    CANVAS_PADDING,
    CANVAS_PADDING + 92,
  );
  context.fillStyle = "#94a3b8";
  context.font = `500 16px ${FONT_FAMILY}`;
  context.fillText(
    `有效对局：${options.totalMatches.toLocaleString()}　Leader：${leaders.length}　排名门槛：${options.minimumGames} 场`,
    CANVAS_PADDING,
    CANVAS_PADDING + 124,
  );
  context.fillStyle = "#64748b";
  context.fillText("横轴为对手，纵轴为我方；少于 5 场标记低样本", CANVAS_PADDING, CANVAS_PADDING + 156);
  context.textAlign = "right";
  context.fillStyle = "#fbbf24";
  context.font = `700 17px ${FONT_FAMILY}`;
  context.fillText(`生成时间：${timestamp.text}`, canvas.width - CANVAS_PADDING, CANVAS_PADDING + 50);
  context.fillStyle = "#64748b";
  context.font = `700 14px ${FONT_FAMILY}`;
  context.fillText("GrandUMI · LEADER MATCHUP MATRIX", canvas.width - CANVAS_PADDING, CANVAS_PADDING + 80);

  const imageEntries = await Promise.all(
    leaders.map(async (leader) => [leader.leaderNumber, await loadLeaderImage(leader.leaderNumber)] as const),
  );
  const images = new Map(imageEntries);
  const rowMap = new Map(options.matrix.rows?.map((row) => [row.leaderNumber, row]) ?? []);

  context.fillStyle = "#111827";
  context.fillRect(tableX, tableY, ROW_HEADER_WIDTH, COLUMN_HEADER_HEIGHT);
  context.strokeStyle = "#374151";
  context.strokeRect(tableX + 0.5, tableY + 0.5, ROW_HEADER_WIDTH - 1, COLUMN_HEADER_HEIGHT - 1);
  context.textAlign = "left";
  context.fillStyle = "#94a3b8";
  context.font = `600 15px ${FONT_FAMILY}`;
  context.fillText("我方 ↓", tableX + 18, tableY + 65);
  context.fillStyle = "#f8fafc";
  context.font = `800 20px ${FONT_FAMILY}`;
  context.fillText("对手 →", tableX + 18, tableY + 96);

  leaders.forEach((leader, columnIndex) => {
    const x = matrixX + columnIndex * LEADER_MATRIX_EXPORT_CELL_WIDTH;
    context.fillStyle = "#111827";
    context.fillRect(x, tableY, LEADER_MATRIX_EXPORT_CELL_WIDTH, COLUMN_HEADER_HEIGHT);
    context.strokeStyle = "#374151";
    context.strokeRect(x + 0.5, tableY + 0.5, LEADER_MATRIX_EXPORT_CELL_WIDTH - 1, COLUMN_HEADER_HEIGHT - 1);
    drawLeaderImage(context, images.get(leader.leaderNumber) ?? null, leader.leaderNumber, x + 32, tableY + 10, 40, 58);
    context.textAlign = "center";
    context.fillStyle = "#cbd5e1";
    context.font = `700 11px ${FONT_FAMILY}`;
    context.fillText(leader.leaderNumber, x + LEADER_MATRIX_EXPORT_CELL_WIDTH / 2, tableY + 88);
    context.fillStyle = "#fdba74";
    context.font = `800 13px ${FONT_FAMILY}`;
    context.fillText(percent(leader.winRate), x + LEADER_MATRIX_EXPORT_CELL_WIDTH / 2, tableY + 112);
    context.fillStyle = "#64748b";
    context.font = `600 10px ${FONT_FAMILY}`;
    context.fillText(`${leader.games} 场`, x + LEADER_MATRIX_EXPORT_CELL_WIDTH / 2, tableY + 134);
  });

  leaders.forEach((leader, rowIndex) => {
    const y = matrixY + rowIndex * LEADER_MATRIX_EXPORT_ROW_HEIGHT;
    const card = getCard(leader.leaderNumber);
    context.fillStyle = "#111827";
    context.fillRect(tableX, y, ROW_HEADER_WIDTH, LEADER_MATRIX_EXPORT_ROW_HEIGHT);
    context.strokeStyle = "#374151";
    context.strokeRect(tableX + 0.5, y + 0.5, ROW_HEADER_WIDTH - 1, LEADER_MATRIX_EXPORT_ROW_HEIGHT - 1);
    context.textAlign = "center";
    context.textBaseline = "middle";
    context.fillStyle = "#fb923c";
    context.font = `800 14px ${FONT_FAMILY}`;
    context.fillText(String(leader.rank ?? "—"), tableX + 20, y + LEADER_MATRIX_EXPORT_ROW_HEIGHT / 2);
    drawLeaderImage(context, images.get(leader.leaderNumber) ?? null, leader.leaderNumber, tableX + 38, y + 9, 42, 62);
    context.textAlign = "left";
    context.fillStyle = "#f1f5f9";
    context.font = `700 13px ${FONT_FAMILY}`;
    const leaderName = card?.name ?? leader.leaderNumber;
    context.fillText(leaderName.length > 15 ? `${leaderName.slice(0, 15)}…` : leaderName, tableX + 92, y + 27);
    context.fillStyle = "#94a3b8";
    context.font = `600 11px ${FONT_FAMILY}`;
    context.fillText(leader.leaderNumber, tableX + 92, y + 47);
    context.fillStyle = "#fdba74";
    context.font = `800 12px ${FONT_FAMILY}`;
    context.fillText(`${percent(leader.winRate)} · ${leader.games} 场`, tableX + 92, y + 66);

    const itemMap = new Map(rowMap.get(leader.leaderNumber)?.items.map((item) => [item.leaderNumber, item]) ?? []);
    leaders.forEach((opponent, columnIndex) => {
      drawMatrixCell(
        context,
        itemMap.get(opponent.leaderNumber),
        matrixX + columnIndex * LEADER_MATRIX_EXPORT_CELL_WIDTH,
        y,
      );
    });
  });

  context.textAlign = "left";
  context.textBaseline = "middle";
  context.fillStyle = "#64748b";
  context.font = `500 12px ${FONT_FAMILY}`;
  context.fillText(
    "颜色仅表示当前行 Leader 对阵对应列 Leader 的胜率区间；镜像对局不计算胜率。",
    CANVAS_PADDING,
    canvas.height - CANVAS_PADDING - 10,
  );
  return canvas;
}

export async function generateLeaderMatchupMatrixImage(
  options: LeaderMatchupMatrixExportOptions,
): Promise<GeneratedLeaderMatchupMatrixImage> {
  const generatedAt = options.generatedAt ?? new Date();
  const timestamp = formatLeaderMatchupMatrixExportTimestamp(generatedAt);
  const canvas = await renderLeaderMatchupMatrixImage({ ...options, generatedAt });
  const blob = await canvasToBlob(canvas);
  return {
    blob,
    filename: `GrandUMI-Leader对阵-${PERIOD_LABELS[options.period]}-${timestamp.filename}.png`,
    width: canvas.width,
    height: canvas.height,
    generatedAtText: timestamp.text,
  };
}

export function downloadLeaderMatchupMatrixImage(
  generated: Pick<GeneratedLeaderMatchupMatrixImage, "blob" | "filename">,
): void {
  if (typeof URL.createObjectURL !== "function" || typeof URL.revokeObjectURL !== "function") {
    throw new Error("当前浏览器不支持图片下载");
  }
  const url = URL.createObjectURL(generated.blob);
  try {
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = generated.filename;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  } catch (error) {
    throw error instanceof Error ? error : new Error("PNG 下载失败");
  } finally {
    // 给浏览器留出接管下载的时间，再释放只用于本次下载的对象 URL。
    window.setTimeout(() => URL.revokeObjectURL(url), 1_000);
  }
}
