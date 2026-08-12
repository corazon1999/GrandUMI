import HomeClient from "./HomeClient";

// 首页包含当前版本入口，必须逐次向源站校验，避免发布后仍引用旧前端脚本。
export const dynamic = "force-dynamic";

export default function HomePage() {
  return <HomeClient />;
}
