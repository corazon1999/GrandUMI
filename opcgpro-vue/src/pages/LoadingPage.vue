<script setup lang="ts">
// 匹配/进场过场（等价旧项目 app/loading/page.tsx）。
</script>

<template>
  <div class="flex h-screen items-center justify-center bg-gray-950">
    <div class="text-center">
      <h1 class="mb-4 text-4xl font-bold text-white">GrandUMI</h1>
      <div class="mx-auto h-1 w-48 overflow-hidden rounded-full bg-gray-800">
        <div class="h-full w-2/3 animate-pulse rounded-full bg-orange-500" />
      </div>
      <p class="mt-4 text-sm text-gray-400">加载中...</p>
    </div>
  </div>
</template>
