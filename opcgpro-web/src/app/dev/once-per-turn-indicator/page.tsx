"use client";

import CardItem from "@/components/ui/CardItem";
import type { CardData } from "@/types/card";

const previewCard = (number: string, name: string, type: CardData["type"]): CardData => ({
  number,
  name,
  type,
  color: "蓝",
  property: "斩",
  power: type === "Stage" ? 0 : 5000,
  cost: type === "Leader" ? 5 : 4,
  keyWords: [],
  counter: 0,
  effectTags: ["ActivatedMain"],
  abilities: [],
  effectEvent: "",
  sprite: `/cards/${number.split("-")[0].toLowerCase()}/${number}.png`,
  sprites: [],
  rarity: "",
  subscript: 0,
  trigger: "",
});

/** 仅开发环境用于桌面/移动端视觉回归；生产环境不提供预览内容。 */
export default function OncePerTurnIndicatorPreview() {
  if (process.env.NODE_ENV === "production") return null;

  const leader = previewCard("OP12-020", "罗罗诺亚·佐罗", "Leader");
  const character = previewCard("OP12-044", "萨卡斯基", "Character");
  const stage = previewCard("OP06-117", "方舟箴言", "Stage");
  return (
    <main className="min-h-screen bg-[#07111f] p-6 text-white">
      <h1 className="mb-5 text-xl font-black">每回合1次标识预览</h1>
      <div className="flex flex-wrap items-center gap-8">
        <CardItem card={leader} size="md" hideCost oncePerTurnEffectAvailable />
        <CardItem card={character} size="md" hideCounter oncePerTurnEffectAvailable />
        <CardItem card={stage} size="md" oncePerTurnEffectAvailable />
        <CardItem card={character} size="md" hideCounter />
      </div>
    </main>
  );
}
