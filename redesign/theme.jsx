// theme.jsx — animated background (theme-aware) + emblem watermark + theme/mood data.

const THEME_KEYS = ['pirate', 'navy'];
const MOOD_KEYS = ['a', 'b', 'c'];
const MOOD_LABEL = { a: '终端', b: '电影', c: '游戏' };

// palette read by the canvas (kept in sync with styles.css themes)
const BG_PALETTE = {
  pirate: { base: '#0e0a06', fog: ['#3a2410', '#5a3410', '#1c1206'], spark: '#f0c463', sonar: false },
  navy:   { base: '#070d18', fog: ['#0f2d52', '#123a66', '#0a1828'], spark: '#7fc0f5', sonar: true },
};

function hexToRgb(h) {
  h = h.replace('#', '');
  return [parseInt(h.slice(0,2),16), parseInt(h.slice(2,4),16), parseInt(h.slice(4,6),16)];
}

// detailed, full-colour anime-style ship crossing the horizon with reflection.
// pirate -> wooden treasure galleon w/ emblem sail & figurehead
// navy   -> steel battleship w/ pagoda bridge, turrets, radar & ensign
// (original designs — not copies of any specific One Piece vessel)
function drawShip(ctx, w, h, t, pal, themeKey) {
  const [sr, sg, sb] = hexToRgb(pal.spark);
  const period = 60;
  const p = ((t / period) % 1 + 1) % 1;
  const x = -260 + p * (w + 520);
  const baseY = h * 0.70;
  const bob = Math.sin(t * 0.6) * 5;
  const tilt = Math.sin(t * 0.6 + 0.5) * 0.022;
  const y = baseY + bob;
  const s = Math.max(1.05, Math.min(1.9, w / 1080));

  const drawBody = (flip) => {
    ctx.save();
    ctx.translate(x, y);
    ctx.rotate(tilt);
    ctx.scale(s, flip ? -s * 0.62 : s);
    if (flip) { ctx.globalAlpha = 0.16; ctx.translate(0, 6); }
    ctx.lineJoin = 'round'; ctx.lineCap = 'round';
    themeKey === 'pirate' ? drawGalleon(ctx, t) : drawWarship(ctx, t);
    ctx.restore();
  };

  ctx.save();
  ctx.globalAlpha = 0.92;
  drawBody(true);   // reflection (under)
  drawBody(false);  // ship
  ctx.restore();

  // bow spray + wake
  ctx.save();
  ctx.globalAlpha = 0.5;
  ctx.strokeStyle = `rgba(${sr},${sg},${sb},0.5)`;
  ctx.lineWidth = 1.6;
  for (let i = 1; i <= 3; i++) {
    ctx.beginPath();
    ctx.ellipse(x - 16 * s, y + 30 * s, 40 * i * s, 6 * i, 0, 0, Math.PI, false);
    ctx.stroke();
  }
  ctx.restore();
}

function grad(ctx, y0, y1, c0, c1) {
  const g = ctx.createLinearGradient(0, y0, 0, y1);
  g.addColorStop(0, c0); g.addColorStop(1, c1); return g;
}

// ── pirate treasure galleon ──────────────────────────────
function drawGalleon(ctx, t) {
  const sw = Math.sin(t * 0.9) * 3;
  // masts
  ctx.strokeStyle = '#3c2a16'; ctx.lineWidth = 4;
  [[-52, -150], [4, -178], [56, -142]].forEach(([mx, top]) => {
    ctx.beginPath(); ctx.moveTo(mx, -6); ctx.lineTo(mx, top); ctx.stroke();
  });
  // yards
  ctx.lineWidth = 3;
  const yards = [[-52,-150],[4,-178],[56,-142]];
  yards.forEach(([mx, top]) => { ctx.beginPath(); ctx.moveTo(mx-36, top+34); ctx.lineTo(mx+36, top+34); ctx.stroke(); });

  // sails (cream, billowed) with red trim
  const sail = (mx, top, ww, hh, emblem) => {
    ctx.fillStyle = grad(ctx, top, top+hh, '#f3ead2', '#cdbf9e');
    ctx.strokeStyle = '#b23b32'; ctx.lineWidth = 2.4;
    ctx.beginPath();
    ctx.moveTo(mx-ww, top+34);
    ctx.quadraticCurveTo(mx+sw, top+48, mx+ww, top+34);
    ctx.lineTo(mx+ww-4, top+hh);
    ctx.quadraticCurveTo(mx+sw, top+hh+16, mx-ww+4, top+hh);
    ctx.closePath(); ctx.fill(); ctx.stroke();
    if (emblem) {
      // jolly-roger: skull + crossed bones (generic)
      const cx = mx + sw*0.4, cy = top + hh*0.55;
      ctx.strokeStyle = '#2a1a0c'; ctx.lineWidth = 4;
      ctx.beginPath(); ctx.moveTo(cx-15, cy+12); ctx.lineTo(cx+15, cy-12); ctx.moveTo(cx+15, cy+12); ctx.lineTo(cx-15, cy-12); ctx.stroke();
      ctx.fillStyle = '#241608';
      ctx.beginPath(); ctx.arc(cx, cy-3, 11, 0, Math.PI*2); ctx.fill();
      ctx.fillStyle = '#f3ead2';
      ctx.beginPath(); ctx.arc(cx-4, cy-4, 2.4, 0, Math.PI*2); ctx.arc(cx+4, cy-4, 2.4, 0, Math.PI*2); ctx.fill();
      ctx.fillStyle = '#241608';
      ctx.fillRect(cx-5, cy+4, 10, 5);
    }
  };
  sail(4, -178, 34, 78, true);     // mainsail w/ emblem
  sail(-52, -150, 28, 64, false);
  sail(56, -142, 26, 60, false);

  // crow's nest on main
  ctx.fillStyle = '#3c2a16';
  ctx.fillRect(-2, -180, 12, 12);

  // pennants (red, fluttering)
  ctx.fillStyle = '#c0392b';
  const flag = (mx, top) => { const f = Math.sin(t*3+mx)*3; ctx.beginPath(); ctx.moveTo(mx, top); ctx.lineTo(mx+26, top+5+f); ctx.lineTo(mx+24, top+9); ctx.lineTo(mx, top+12); ctx.closePath(); ctx.fill(); };
  flag(-52,-150); flag(4,-178); flag(56,-142);

  // bowsprit + jib
  ctx.strokeStyle = '#3c2a16'; ctx.lineWidth = 3;
  ctx.beginPath(); ctx.moveTo(92, -8); ctx.lineTo(140, -26); ctx.stroke();
  ctx.fillStyle = 'rgba(243,234,210,0.85)'; ctx.strokeStyle = '#b23b32'; ctx.lineWidth = 2;
  ctx.beginPath(); ctx.moveTo(60, -10); ctx.lineTo(136, -24); ctx.lineTo(66, 4); ctx.closePath(); ctx.fill(); ctx.stroke();

  // hull (wood) with raised stern castle (left) + figurehead bow (right)
  ctx.fillStyle = grad(ctx, -34, 34, '#9a6531', '#4a2f17');
  ctx.strokeStyle = '#2a1a0c'; ctx.lineWidth = 2.4;
  ctx.beginPath();
  ctx.moveTo(-112, 4);
  ctx.lineTo(-112, -34);
  ctx.lineTo(-74, -38);
  ctx.lineTo(-70, 4);
  ctx.lineTo(92, 4);
  ctx.lineTo(118, -4);                      // bow toward figurehead
  ctx.quadraticCurveTo(130, 12, 110, 20);
  ctx.lineTo(92, 34);
  ctx.lineTo(-66, 38);
  ctx.quadraticCurveTo(-110, 36, -112, 12);
  ctx.closePath(); ctx.fill(); ctx.stroke();

  // gold gunwale stripe
  ctx.strokeStyle = '#e8b04b'; ctx.lineWidth = 5;
  ctx.beginPath(); ctx.moveTo(-110, 6); ctx.lineTo(112, 4); ctx.stroke();

  // gun ports (gold-framed)
  for (let gx = -56; gx <= 70; gx += 22) {
    ctx.fillStyle = '#1c1208'; ctx.fillRect(gx, 16, 9, 9);
    ctx.strokeStyle = '#e8b04b'; ctx.lineWidth = 1.4; ctx.strokeRect(gx, 16, 9, 9);
  }
  // stern windows
  ctx.fillStyle = '#f5cd72';
  for (let i=0;i<3;i++) ctx.fillRect(-106 + i*11, -28, 7, 12);

  // figurehead (stylised dragon/fish head — original)
  ctx.fillStyle = '#e8b04b'; ctx.strokeStyle = '#2a1a0c'; ctx.lineWidth = 1.8;
  ctx.beginPath();
  ctx.moveTo(116, -2);
  ctx.quadraticCurveTo(140, -10, 150, 2);
  ctx.quadraticCurveTo(140, 4, 138, 12);
  ctx.quadraticCurveTo(128, 6, 116, 8);
  ctx.closePath(); ctx.fill(); ctx.stroke();
}

// ── steel battleship ─────────────────────────────────────
function drawWarship(ctx, t) {
  const steel = grad(ctx, -8, 26, '#79858f', '#2c353d');
  const upper = grad(ctx, -40, 4, '#c9d2d9', '#7c8893');

  // pagoda bridge (battleship-style stacked tower)
  ctx.fillStyle = upper; ctx.strokeStyle = '#1b2228'; ctx.lineWidth = 1.8;
  const tier = (cx, wY, wB, yT, yB) => { ctx.beginPath(); ctx.moveTo(cx-wY, yT); ctx.lineTo(cx+wY, yT); ctx.lineTo(cx+wB, yB); ctx.lineTo(cx-wB, yB); ctx.closePath(); ctx.fill(); ctx.stroke(); };
  tier(-18, 8, 12, -64, -44);
  tier(-18, 12, 18, -44, -22);
  tier(-18, 18, 26, -22, 2);
  // radar/lattice mast
  ctx.strokeStyle = '#cdd6dc'; ctx.lineWidth = 2;
  ctx.beginPath(); ctx.moveTo(-18, -64); ctx.lineTo(-18, -90); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(-30, -78); ctx.lineTo(-6, -78); ctx.stroke();
  ctx.beginPath(); ctx.ellipse(-18, -92, 7, 3, 0, 0, Math.PI*2); ctx.stroke();
  // small flag on mast
  ctx.fillStyle = '#c0392b'; const f = Math.sin(t*3)*2;
  ctx.beginPath(); ctx.moveTo(-18,-90); ctx.lineTo(-2,-86+f); ctx.lineTo(-18,-82); ctx.closePath(); ctx.fill();

  // funnels
  ctx.fillStyle = '#3a444d'; ctx.strokeStyle = '#1b2228'; ctx.lineWidth = 1.8;
  [[14, 18], [40, 16]].forEach(([fx, fw]) => { ctx.beginPath(); ctx.moveTo(fx-fw/2, 2); ctx.lineTo(fx-fw/2+3, -30); ctx.lineTo(fx+fw/2-3, -30); ctx.lineTo(fx+fw/2, 2); ctx.closePath(); ctx.fill(); ctx.stroke(); });

  // gun turrets w/ twin barrels
  ctx.fillStyle = steel;
  const turret = (tx, dir, th) => {
    ctx.beginPath(); ctx.moveTo(tx-16, 2); ctx.lineTo(tx-12, 2-th); ctx.lineTo(tx+12, 2-th); ctx.lineTo(tx+16, 2); ctx.closePath(); ctx.fill(); ctx.stroke();
    ctx.strokeStyle = '#1b2228'; ctx.lineWidth = 3;
    ctx.beginPath();
    ctx.moveTo(tx+ (dir>0?6:-6), 2-th+2); ctx.lineTo(tx+dir*34, 2-th-2);
    ctx.moveTo(tx+ (dir>0?6:-6), 2-th+6); ctx.lineTo(tx+dir*34, 2-th+2);
    ctx.stroke(); ctx.lineWidth = 1.8; ctx.fillStyle = steel;
  };
  turret(66, 1, 16);
  turret(92, 1, 12);
  turret(-66, -1, 16);

  // hull (steel, long, raked bow right) + red waterline
  ctx.fillStyle = steel; ctx.strokeStyle = '#1b2228'; ctx.lineWidth = 2.2;
  ctx.beginPath();
  ctx.moveTo(-122, 2);
  ctx.lineTo(104, 2);
  ctx.lineTo(132, 6);          // bow
  ctx.lineTo(110, 24);
  ctx.lineTo(-108, 24);
  ctx.lineTo(-122, 2);
  ctx.closePath(); ctx.fill(); ctx.stroke();
  // waterline stripe
  ctx.strokeStyle = '#b8392f'; ctx.lineWidth = 4;
  ctx.beginPath(); ctx.moveTo(-112, 22); ctx.lineTo(116, 22); ctx.stroke();
  // deck line
  ctx.strokeStyle = '#cdd6dc'; ctx.lineWidth = 1.6;
  ctx.beginPath(); ctx.moveTo(-110, 2); ctx.lineTo(120, 2); ctx.stroke();
  // portholes
  ctx.fillStyle = '#0f1419';
  for (let gx=-96; gx<=96; gx+=20) { ctx.beginPath(); ctx.arc(gx, 13, 2.4, 0, Math.PI*2); ctx.fill(); }

  // stern ensign
  ctx.strokeStyle = '#cdd6dc'; ctx.lineWidth = 2;
  ctx.beginPath(); ctx.moveTo(-118, 2); ctx.lineTo(-118, -20); ctx.stroke();
  ctx.fillStyle = '#eef3f7';
  ctx.beginPath(); ctx.moveTo(-118,-20); ctx.lineTo(-98,-16); ctx.lineTo(-118,-10); ctx.closePath(); ctx.fill();
  ctx.strokeStyle = '#b8392f'; ctx.lineWidth = 1.4;
  ctx.beginPath(); ctx.moveTo(-114,-18); ctx.lineTo(-104,-13); ctx.stroke();
}

// Canvas: drifting fog blobs + rising motes/embers + (navy) sonar rings + ripples.
function AnimatedBackground({ themeKey }) {
  const canvasRef = React.useRef(null);
  const stateRef = React.useRef({ t: 0, motes: [], rings: [] });

  React.useEffect(() => {
    const canvas = canvasRef.current;
    const ctx = canvas.getContext('2d');
    let raf, w, h, dpr;
    const S = stateRef.current;

    const resize = () => {
      dpr = Math.min(window.devicePixelRatio || 1, 2);
      w = canvas.clientWidth; h = canvas.clientHeight;
      canvas.width = w * dpr; canvas.height = h * dpr;
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    };
    resize();
    const ro = new ResizeObserver(resize); ro.observe(canvas);

    // init motes
    S.motes = Array.from({ length: 46 }, () => ({
      x: Math.random(), y: Math.random(),
      r: 0.6 + Math.random() * 2.2, sp: 0.06 + Math.random() * 0.16,
      drift: (Math.random() - 0.5) * 0.4, ph: Math.random() * Math.PI * 2,
    }));
    S.rings = [];

    let last = performance.now();
    const draw = (now) => {
      const dt = Math.min(0.05, (now - last) / 1000); last = now;
      S.t += dt;
      const pal = BG_PALETTE[themeKey];
      const [br, bg, bb] = hexToRgb(pal.base);

      ctx.fillStyle = pal.base;
      ctx.fillRect(0, 0, w, h);

      // drifting fog blobs
      ctx.globalCompositeOperation = 'lighter';
      const blobs = [
        { cx: 0.28, cy: 0.30, col: pal.fog[0], rad: 0.55, sx: 0.05, sy: 0.03 },
        { cx: 0.74, cy: 0.62, col: pal.fog[1], rad: 0.5, sx: -0.04, sy: 0.05 },
        { cx: 0.52, cy: 0.85, col: pal.fog[2], rad: 0.6, sx: 0.03, sy: -0.04 },
      ];
      blobs.forEach((b, i) => {
        const px = (b.cx + Math.sin(S.t * b.sx + i) * 0.06) * w;
        const py = (b.cy + Math.cos(S.t * b.sy + i) * 0.06) * h;
        const R = b.rad * Math.max(w, h) * 0.7;
        const g = ctx.createRadialGradient(px, py, 0, px, py, R);
        const [r, gr, bl] = hexToRgb(b.col);
        const a = 0.30 + Math.sin(S.t * 0.3 + i) * 0.06;
        g.addColorStop(0, `rgba(${r},${gr},${bl},${a})`);
        g.addColorStop(1, `rgba(${r},${gr},${bl},0)`);
        ctx.fillStyle = g; ctx.beginPath(); ctx.arc(px, py, R, 0, Math.PI * 2); ctx.fill();
      });

      // sonar rings (navy)
      if (pal.sonar) {
        if (Math.random() < dt * 0.55) {
          S.rings.push({ x: 0.5 + (Math.random()-0.5)*0.5, y: 0.5 + (Math.random()-0.5)*0.5, life: 0 });
        }
        const [sr, sg, sb] = hexToRgb(pal.spark);
        S.rings = S.rings.filter(r => r.life < 1);
        S.rings.forEach(r => {
          r.life += dt * 0.22;
          const rad = r.life * Math.min(w, h) * 0.32;
          ctx.strokeStyle = `rgba(${sr},${sg},${sb},${(1 - r.life) * 0.35})`;
          ctx.lineWidth = 1.4;
          ctx.beginPath(); ctx.arc(r.x * w, r.y * h, rad, 0, Math.PI * 2); ctx.stroke();
        });
      }

      // rising motes / embers
      const [sr, sg, sb] = hexToRgb(pal.spark);
      S.motes.forEach(m => {
        m.y -= m.sp * dt * (themeKey === 'pirate' ? 0.9 : 0.6);
        m.x += Math.sin(S.t * 0.5 + m.ph) * m.drift * dt;
        if (m.y < -0.03) { m.y = 1.03; m.x = Math.random(); }
        const o = (0.35 + Math.sin(S.t + m.ph) * 0.3) * (themeKey === 'pirate' ? 0.9 : 0.7);
        ctx.fillStyle = `rgba(${sr},${sg},${sb},${Math.max(0, o)})`;
        ctx.beginPath(); ctx.arc(m.x * w, m.y * h, m.r, 0, Math.PI * 2); ctx.fill();
      });

      ctx.globalCompositeOperation = 'source-over';

      // bottom water ripple lines
      const rg = ctx.createLinearGradient(0, h * 0.62, 0, h);
      rg.addColorStop(0, `rgba(${sr},${sg},${sb},0)`);
      rg.addColorStop(1, `rgba(${sr},${sg},${sb},0.05)`);
      ctx.fillStyle = rg; ctx.fillRect(0, h * 0.62, w, h * 0.38);
      ctx.strokeStyle = `rgba(${sr},${sg},${sb},0.10)`; ctx.lineWidth = 1;
      for (let k = 0; k < 3; k++) {
        const yy = h * (0.80 + k * 0.06);
        ctx.beginPath();
        for (let x = 0; x <= w; x += 14) {
          const y = yy + Math.sin(x * 0.012 + S.t * (0.8 + k * 0.3) + k) * (5 + k * 2);
          x === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
        }
        ctx.stroke();
      }

      // vignette
      const vg = ctx.createRadialGradient(w/2, h*0.42, h*0.2, w/2, h*0.5, Math.max(w,h)*0.75);
      vg.addColorStop(0, 'rgba(0,0,0,0)');
      vg.addColorStop(1, `rgba(${br},${bg},${bb},0.85)`);
      ctx.fillStyle = vg; ctx.fillRect(0, 0, w, h);

      raf = requestAnimationFrame(draw);
    };
    raf = requestAnimationFrame(draw);
    return () => { cancelAnimationFrame(raf); ro.disconnect(); };
  }, [themeKey]);

  return (
    <div style={{ position: 'absolute', inset: 0, zIndex: 0 }}>
      <canvas ref={canvasRef} style={{ width: '100%', height: '100%', display: 'block' }} />
      <EmblemWatermark themeKey={themeKey} />
    </div>
  );
}

// faint, slowly-breathing crest behind content. Built from simple shapes/strokes only.
function EmblemWatermark({ themeKey }) {
  return (
    <div style={{
      position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center',
      pointerEvents: 'none', opacity: 0.06, animation: 'breathe 9s ease-in-out infinite',
    }}>
      <svg width="640" height="640" viewBox="0 0 200 200" fill="none"
           stroke="var(--primary)" strokeWidth="1.4">
        <circle cx="100" cy="100" r="86" strokeDasharray="3 6" />
        <circle cx="100" cy="100" r="70" />
        {themeKey === 'pirate' ? (
          <g strokeWidth="3" strokeLinecap="round">
            {/* crossed sabres */}
            <line x1="58" y1="58" x2="142" y2="142" />
            <line x1="142" y1="58" x2="58" y2="142" />
            <circle cx="100" cy="100" r="20" strokeWidth="2" />
            <circle cx="92" cy="96" r="3.5" strokeWidth="2" />
            <circle cx="108" cy="96" r="3.5" strokeWidth="2" />
          </g>
        ) : (
          <g strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
            {/* anchor */}
            <circle cx="100" cy="58" r="8" strokeWidth="2.5" />
            <line x1="100" y1="66" x2="100" y2="140" />
            <line x1="74" y1="86" x2="126" y2="86" />
            <path d="M64 112 C64 138 84 146 100 146 C116 146 136 138 136 112" />
            <line x1="60" y1="106" x2="68" y2="116" />
            <line x1="140" y1="106" x2="132" y2="116" />
          </g>
        )}
      </svg>
    </div>
  );
}

Object.assign(window, { THEME_KEYS, MOOD_KEYS, MOOD_LABEL, AnimatedBackground, EmblemWatermark });
