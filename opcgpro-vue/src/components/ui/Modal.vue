<script setup lang="ts">
// 通用模态框。等价旧项目 ui/Modal.tsx（framer AnimatePresence → Vue <Transition>）。
defineProps<{ open: boolean; title?: string }>();
const emit = defineEmits<{ (e: "close"): void }>();
</script>

<template>
  <Transition name="modal-fade">
    <div v-if="open" class="fixed inset-0 z-50 flex items-center justify-center">
      <div class="absolute inset-0 bg-black/60 backdrop-blur-sm" @click="emit('close')" />
      <div
        class="modal-card relative z-10 min-w-80 max-w-lg rounded-xl border border-gray-700 bg-gray-900 p-6 shadow-2xl"
      >
        <h2 v-if="title" class="mb-4 text-lg font-bold text-white">{{ title }}</h2>
        <slot />
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.modal-fade-enter-active,
.modal-fade-leave-active {
  transition: opacity 0.18s ease;
}
.modal-fade-enter-from,
.modal-fade-leave-to {
  opacity: 0;
}
.modal-fade-enter-active .modal-card,
.modal-fade-leave-active .modal-card {
  transition: transform 0.18s ease;
}
.modal-fade-enter-from .modal-card,
.modal-fade-leave-to .modal-card {
  transform: translateY(20px) scale(0.9);
}
</style>
