export type CardType = "Leader" | "Character" | "Stage" | "Event";
export type CardColor = "炎" | "风" | "水" | "暗" | "地" | "光" | string;
export type CardProperty = "斩" | "打" | "射" | "智" | "特";

export interface CardData {
  number: string;
  name: string;
  color: CardColor;
  type: CardType;
  property: CardProperty;
  power: number;
  cost: number;
  keyWords: string[];
  counter: number;
  effectTags: string[];   // 触发时机枚举名（如 "OnEnterField", "ActivatedMain"）
  abilities: string[];    // 能力关键字（阻挡者/速攻/双重攻击/…）
  effectText: string;
  effectEvent: string;
  sprite?: string;
  image?: string;      // 原始图源 URL（windoent），本地缩略图/原图缺失时回退
  sprites: string[];   // 所有版本（正画在 [0]，异画依次排后）
  rarity: string;      // 稀有度: L/R/UC/C/SR/SEC/P
  subscript: number;   // 角标属性
  trigger: string;     // 触发效果文本
}
