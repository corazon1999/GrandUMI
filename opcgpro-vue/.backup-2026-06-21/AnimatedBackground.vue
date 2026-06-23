<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount, watch } from "vue";

const props = withDefaults(defineProps<{ themeKey?: "pirate" | "navy" }>(), {
  themeKey: "pirate",
});

const canvasRef = ref<HTMLCanvasElement | null>(null);

// 响应式 palette：跟随 props.themeKey 变化
const palette = ref<"pirate" | "navy">(props.themeKey);
watch(() => props.themeKey, (v) => {
  palette.value = v ?? "pirate";
  // 重置环（navy 主题专属）
  if (state) state.rings.length = 0;
});

const BG_PALETTE = {
  pirate: {
    base: "#0e0a06",
    fog: ["#3a2410", "#5a3410", "#1c1206"] as [string, string, string],
    spark: "#f0c463",
    sonar: false,
  },
  navy: {
    base: "#070d18",
    fog: ["#0f2d52", "#123a66", "#0a1828"] as [string, string, string],
    spark: "#7fc0f5",
    sonar: true,
  },
};

function hexToRgb(h: string): [number, number, number] {
  const s = h.replace("#", "");
  return [parseInt(s.slice(0, 2), 16), parseInt(s.slice(2, 4), 16), parseInt(s.slice(4, 6), 16)];
}

let raf: number | null = null;
let ro: ResizeObserver | null = null;
let state: {
  t: number;
  motes: { x: number; y: number; r: number; sp: number; drift: number; ph: number }[];
  rings: { x: number; y: number; life: number }[];
} | null = null;

onMounted(() => {
  const canvas = canvasRef.value!;
  const ctx = canvas.getContext("2d")!;
  let w = 0, h = 0;

  state = {
    t: 0,
    motes: Array.from({ length: 46 }, () => ({
      x: Math.random(),
      y: Math.random(),
      r: 0.6 + Math.random() * 2.2,
      sp: 0.06 + Math.random() * 0.16,
      drift: (Math.random() - 0.5) * 0.4,
      ph: Math.random() * Math.PI * 2,
    })),
    rings: [],
  };

  const resize = () => {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    w = canvas.clientWidth;
    h = canvas.clientHeight;
    canvas.width = w * dpr;
    canvas.height = h * dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  };
  resize();
  ro = new ResizeObserver(resize);
  ro.observe(canvas);

  let last = performance.now();

  const draw = (now: number) => {
    if (!state) return;
    const dt = Math.min(0.05, (now - last) / 1000);
    last = now;
    state.t += dt;

    const pal = BG_PALETTE[palette.value];
    const [br, bg, bb] = hexToRgb(pal.base);

    ctx.fillStyle = pal.base;
    ctx.fillRect(0, 0, w, h);

    // drifting fog blobs
    ctx.globalCompositeOperation = "lighter";
    const blobs = [
      { cx: 0.28, cy: 0.30, col: pal.fog[0], rad: 0.55, sx: 0.05, sy: 0.03 },
      { cx: 0.74, cy: 0.62, col: pal.fog[1], rad: 0.5,  sx: -0.04, sy: 0.05 },
      { cx: 0.52, cy: 0.85, col: pal.fog[2], rad: 0.6,  sx: 0.03,  sy: -0.04 },
    ];
    blobs.forEach((b, i) => {
      const px = (b.cx + Math.sin(state!.t * b.sx + i) * 0.06) * w;
      const py = (b.cy + Math.cos(state!.t * b.sy + i) * 0.06) * h;
      const R  = b.rad * Math.max(w, h) * 0.7;
      const g  = ctx.createRadialGradient(px, py, 0, px, py, R);
      const [r, gr, bl] = hexToRgb(b.col);
      const a  = 0.30 + Math.sin(state!.t * 0.3 + i) * 0.06;
      g.addColorStop(0, `rgba(${r},${gr},${bl},${a})`);
      g.addColorStop(1, `rgba(${r},${gr},${bl},0)`);
      ctx.fillStyle = g;
      ctx.beginPath();
      ctx.arc(px, py, R, 0, Math.PI * 2);
      ctx.fill();
    });

    // sonar rings (navy only)
    if (pal.sonar) {
      if (Math.random() < dt * 0.55) {
        state!.rings.push({
          x: 0.5 + (Math.random() - 0.5) * 0.5,
          y: 0.5 + (Math.random() - 0.5) * 0.5,
          life: 0,
        });
      }
      const [sr, sg, sb] = hexToRgb(pal.spark);
      state!.rings = state!.rings.filter((r) => r.life < 1);
      state!.rings.forEach((r) => {
        r.life += dt * 0.22;
        const rad = r.life * Math.min(w, h) * 0.32;
        ctx.strokeStyle = `rgba(${sr},${sg},${sb},${(1 - r.life) * 0.35})`;
        ctx.lineWidth = 1.4;
        ctx.beginPath();
        ctx.arc(r.x * w, r.y * h, rad, 0, Math.PI * 2);
        ctx.stroke();
      });
    }

    // rising motes / embers
    const [sr, sg, sb] = hexToRgb(pal.spark);
    state!.motes.forEach((m) => {
      m.y -= m.sp * dt * (palette.value === "pirate" ? 0.9 : 0.6);
      m.x += Math.sin(state!.t * 0.5 + m.ph) * m.drift * dt;
      if (m.y < -0.03) { m.y = 1.03; m.x = Math.random(); }
      const o = (0.35 + Math.sin(state!.t + m.ph) * 0.3) * (palette.value === "pirate" ? 0.9 : 0.7);
      ctx.fillStyle = `rgba(${sr},${sg},${sb},${Math.max(0, o)})`;
      ctx.beginPath();
      ctx.arc(m.x * w, m.y * h, m.r, 0, Math.PI * 2);
      ctx.fill();
    });

    ctx.globalCompositeOperation = "source-over";

    // bottom water ripple lines
    const rg = ctx.createLinearGradient(0, h * 0.62, 0, h);
    rg.addColorStop(0, `rgba(${sr},${sg},${sb},0)`);
    rg.addColorStop(1, `rgba(${sr},${sg},${sb},0.05)`);
    ctx.fillStyle = rg;
    ctx.fillRect(0, h * 0.62, w, h * 0.38);
    ctx.strokeStyle = `rgba(${sr},${sg},${sb},0.10)`;
    ctx.lineWidth = 1;
    for (let k = 0; k < 3; k++) {
      const yy = h * (0.80 + k * 0.06);
      ctx.beginPath();
      for (let x = 0; x <= w; x += 14) {
        const y = yy + Math.sin(x * 0.012 + state!.t * (0.8 + k * 0.3) + k) * (5 + k * 2);
        x === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
      }
      ctx.stroke();
    }

    // vignette
    const vg = ctx.createRadialGradient(w / 2, h * 0.42, h * 0.2, w / 2, h * 0.5, Math.max(w, h) * 0.75);
    vg.addColorStop(0, "rgba(0,0,0,0)");
    vg.addColorStop(1, `rgba(${br},${bg},${bb},0.85)`);
    ctx.fillStyle = vg;
    ctx.fillRect(0, 0, w, h);

    raf = requestAnimationFrame(draw);
  };

  raf = requestAnimationFrame(draw);
});

onBeforeUnmount(() => {
  if (raf) cancelAnimationFrame(raf);
  if (ro) ro.disconnect();
});
</script>

<template>
  <canvas ref="canvasRef" style="width: 100%; height: 100%; display: block" />
</template>
