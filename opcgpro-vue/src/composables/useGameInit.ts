import { ref, watch } from "vue";
import { useStore } from "./useStore";
import { useGameStore } from "@/store/gameStore";
import { getCard, loadCardSet, loadAllCards, applySpriteMap } from "@/data/CardLoader";
import { CARD_SET_PATHS } from "@/data/cardSets";

/**
 * 游戏对局初始化（服务器权威）。
 * 等首份 MsgGameState（my != null），按需懒加载快照里出现的卡集 JSON。
 * 同时后台全量加载全部卡数据，并应用异画映射。
 * 返回响应式 ready ref。
 */
export function useGameInit() {
  const ready = ref(false);
  const my = useStore(useGameStore, (s) => s.my);
  const opp = useStore(useGameStore, (s) => s.opponent);
  let kickedFullLoad = false;

  watch(
    [my, opp],
    () => {
      const m = my.value;
      const o = opp.value;
      if (!m || !o) return;

      const allNums = [
        m.leaderNumber, o.leaderNumber,
        ...m.handCardNumbers,
        ...m.fieldCards.map((c) => c.number),
        ...o.fieldCards.map((c) => c.number),
        ...m.trashNumbers, ...o.trashNumbers,
        m.stageNumber, o.stageNumber,
      ].filter((n): n is string => !!n);

      const missing = new Set<string>();
      for (const n of allNums) {
        if (!getCard(n)) {
          const setCode = n.split("-")[0];
          if (setCode in CARD_SET_PATHS) missing.add(setCode);
        }
      }

      if (missing.size === 0) {
        applySessionSpriteMap();
        ready.value = true;
      } else {
        Promise.all([...missing].map((p) => loadCardSet(p).catch(() => {}))).then(() => {
          applySessionSpriteMap();
          ready.value = true;
        });
      }

      // 后台一次性全量加载全部卡数据：牌库内容不在快照里，抽到未加载卡集的牌时
      // getCard 会返回空而显示卡背。预先全量加载杜绝此问题。
      if (!kickedFullLoad) {
        kickedFullLoad = true;
        loadAllCards()
          .then(() => applySessionSpriteMap())
          .catch(() => { kickedFullLoad = false; });
      }
    },
    { immediate: true },
  );

  return ready;
}

/** 从 sessionStorage 读取异画映射并应用到卡牌缓存 */
function applySessionSpriteMap() {
  try {
    const raw = sessionStorage.getItem("grandumi_spriteMap");
    if (raw) {
      const map = JSON.parse(raw) as Record<string, string>;
      applySpriteMap(map);
    }
  } catch {
    // 解析失败时忽略，使用默认原画
  }
}
