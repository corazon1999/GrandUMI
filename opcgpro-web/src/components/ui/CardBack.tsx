import { clsx } from "clsx";
import { cardBackImageSrc, cardBackName, normalizeCardBackId } from "@/lib/cardBacks";

const themes = {
  classic: {
    surface: "from-sky-950 via-blue-950 to-slate-950",
    border: "border-sky-200/55",
    inner: "border-sky-300/30",
    halo: "bg-sky-300/15 ring-sky-200/35",
    mark: "text-sky-100",
    accent: "bg-sky-300/30",
  },
  "straw-hat": {
    surface: "from-amber-500 via-red-700 to-red-950",
    border: "border-amber-100/70",
    inner: "border-amber-200/45",
    halo: "bg-amber-200/20 ring-amber-100/55",
    mark: "text-amber-50",
    accent: "bg-amber-200/45",
  },
  marine: {
    surface: "from-blue-500 via-blue-800 to-slate-950",
    border: "border-blue-100/75",
    inner: "border-white/40",
    halo: "bg-white/15 ring-blue-100/55",
    mark: "text-blue-50",
    accent: "bg-white/35",
  },
  emperor: {
    surface: "from-fuchsia-700 via-violet-950 to-slate-950",
    border: "border-fuchsia-100/65",
    inner: "border-fuchsia-200/35",
    halo: "bg-fuchsia-200/15 ring-fuchsia-100/45",
    mark: "text-fuchsia-50",
    accent: "bg-fuchsia-200/35",
  },
} as const;

export default function CardBack({
  cardBackId,
  className,
  decorative = false,
}: {
  cardBackId?: string | null;
  className?: string;
  decorative?: boolean;
}) {
  const id = normalizeCardBackId(cardBackId);
  const customImage = cardBackImageSrc(id);
  if (customImage) {
    return (
      <div
        className={clsx("relative h-full w-full overflow-hidden rounded-[inherit] border-2 border-white/35 bg-gray-950 shadow-inner", className)}
        role={decorative ? undefined : "img"}
        aria-hidden={decorative || undefined}
        aria-label={decorative ? undefined : `${cardBackName(id)}卡背`}
      >
        <img src={customImage} alt="" draggable={false} className="h-full w-full object-cover" />
        <div className="pointer-events-none absolute inset-[4%] rounded-[8%] border border-white/20" />
      </div>
    );
  }
  const theme = themes[id as keyof typeof themes] ?? themes.classic;

  return (
    <div
      className={clsx(
        "relative h-full w-full overflow-hidden rounded-[inherit] border-2 bg-gradient-to-br shadow-inner",
        theme.surface,
        theme.border,
        className,
      )}
      role={decorative ? undefined : "img"}
      aria-hidden={decorative || undefined}
      aria-label={decorative ? undefined : `${cardBackName(id)}卡背`}
    >
      <div className={clsx("absolute inset-[7%] rounded-[10%] border", theme.inner)} />
      <div className="absolute inset-0 opacity-45 [background-image:repeating-linear-gradient(135deg,transparent_0_9px,rgba(255,255,255,0.12)_10px_11px,transparent_12px_20px)]" />
      <div
        className={clsx(
          "absolute left-1/2 top-1/2 grid aspect-square w-[58%] -translate-x-1/2 -translate-y-1/2 place-items-center rounded-full ring-1",
          theme.halo,
        )}
      >
        <span className={clsx("font-black tracking-tighter drop-shadow", theme.mark)}>G</span>
      </div>
      <span className={clsx("absolute left-1/2 top-[13%] h-[2px] w-[36%] -translate-x-1/2 rounded-full", theme.accent)} />
      <span className={clsx("absolute bottom-[13%] left-1/2 h-[2px] w-[36%] -translate-x-1/2 rounded-full", theme.accent)} />
    </div>
  );
}
