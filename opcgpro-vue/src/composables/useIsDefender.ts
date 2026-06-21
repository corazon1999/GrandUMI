import { computed } from "vue";
import { useStore } from "@/composables/useStore";
import { useGameStore } from "@/store/gameStore";

/**
 * 判断「我是否为当前这场战斗的防守方」。
 * 防守方 = 攻击者卡属于对手；不依赖 currentTurn，兼容 GM「对手领袖攻击」场景。
 */
export function useIsDefender() {
  const battle = useStore(useGameStore, (s) => s.battle);
  const opp = useStore(useGameStore, (s) => s.opponent);
  return computed(() => {
    if (!battle.value || !opp.value) return false;
    return (
      battle.value.attackerCardId === opp.value.leaderId ||
      opp.value.fieldCards.some((c) => c.id === battle.value!.attackerCardId)
    );
  });
}
