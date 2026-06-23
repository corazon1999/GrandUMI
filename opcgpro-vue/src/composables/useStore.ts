import { shallowRef, onScopeDispose, type Ref } from "vue";
import type { StoreApi } from "zustand/vanilla";

/**
 * useStore — 把 zustand vanilla store 接入 Vue 响应式系统。
 *
 * 取代 React 版的 `useXStore(s => s.x)` 写法：
 *   const phase = useStore(useGameStore, s => s.phase);  // -> Readonly<Ref>
 * 模板中用 `phase.value` 或直接 `{{ phase }}`。
 *
 * action 调用仍走 `useGameStore.getState().setPending(true)`，不经此桥接。
 */

/** 订阅 store 的一个切片，返回只读响应式 ref；scope 销毁时自动退订。 */
export function useStore<T, U>(store: StoreApi<T>, selector: (s: T) => U): Readonly<Ref<U>>;
/** 不带 selector：订阅整个 state。 */
export function useStore<T>(store: StoreApi<T>): Readonly<Ref<T>>;
export function useStore<T, U>(store: StoreApi<T>, selector?: (s: T) => U) {
  const select = selector ?? ((s: T) => s as unknown as U);
  const sliceRef = shallowRef(select(store.getState())) as Ref<U>;
  const unsub = store.subscribe((state) => {
    const next = select(state);
    if (!Object.is(next, sliceRef.value)) sliceRef.value = next;
  });
  onScopeDispose(unsub);
  return sliceRef;
}
