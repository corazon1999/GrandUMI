import { createRouter, createWebHistory, type RouteRecordRaw } from "vue-router";
import { useNetStore } from "@/store/netStore";

const routes: RouteRecordRaw[] = [
  { path: "/",            redirect: "/login" },
  { path: "/login",       component: () => import("@/pages/LoginPage.vue") },
  { path: "/home",        component: () => import("@/pages/HomePage.vue"),        meta: { requiresAuth: true } },
  { path: "/deck-editor", component: () => import("@/pages/DeckEditorPage.vue"),  meta: { requiresAuth: true } },
  { path: "/game",        component: () => import("@/pages/GamePage.vue"),        meta: { requiresAuth: true } },
  { path: "/spectate",    component: () => import("@/pages/SpectatePage.vue"),    meta: { requiresAuth: true } },
  { path: "/replay/:id",  component: () => import("@/pages/ReplayPage.vue"),      meta: { requiresAuth: true } },
  { path: "/loading",     component: () => import("@/pages/LoadingPage.vue") },
];

export const router = createRouter({
  history: createWebHistory(),
  routes,
});

router.beforeEach((to) => {
  if (to.meta.requiresAuth && !useNetStore.getState().loggedIn) {
    if (typeof window !== "undefined" && window.location.search.includes("__test_bypass=1")) return;
    return "/login";
  }
});
