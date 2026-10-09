import { create } from "zustand";

export interface PublicPlayerIdentity {
  name: string;
  equippedSeasonTitle: string | null;
}

export const usePlayerIdentityStore = create<{
  identities: Record<string, PublicPlayerIdentity>;
  merge: (identities: PublicPlayerIdentity[]) => void;
  reset: () => void;
}>((set) => ({
  identities: Object.create(null),
  merge: (items) => set((state) => {
    const identities: Record<string, PublicPlayerIdentity> = Object.assign(Object.create(null), state.identities);
    for (const item of items) {
      if (!item || typeof item.name !== "string" || !item.name || item.name.length > 32) continue;
      if (item.equippedSeasonTitle !== null && typeof item.equippedSeasonTitle !== "string") continue;
      identities[item.name] = { name: item.name, equippedSeasonTitle: item.equippedSeasonTitle };
    }
    return { identities };
  }),
  reset: () => set({ identities: Object.create(null) }),
}));
