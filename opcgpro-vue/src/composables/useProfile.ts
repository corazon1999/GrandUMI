import { reactive } from "vue";

/**
 * 个人中心档案。头像沿用项目原有「图片头像」功能：从领航卡卡图中选择，
 * 存的是 sprite 图片 URL（兼容旧键 `grandumi_avatar`）。其余昵称/称号/等级/ID 仅存本设备。
 * 响应式单例，Sidebar 与 ProfilePanel 共享。
 */
export interface Profile {
  name: string;
  avatar: string; // 头像图片 sprite URL（空 = 运行时回退默认领航卡）
  title: string;
  lv: number;
  id: string;
}

export const TITLES = [
  "见习航海士", "草帽船员", "百兽海贼团", "海军本部",
  "王下七武海", "革命军", "超新星", "四皇候补",
];

export const DEFAULT_PROFILE: Profile = {
  name: "CAPTAIN",
  avatar: "",
  title: "见习航海士",
  lv: 12,
  id: "880123",
};

const LS_KEY = "grandumi-profile";
const LEGACY_AVATAR_KEY = "grandumi_avatar"; // 旧图片头像功能的键，保持兼容互通

function load(): Profile {
  let p: Profile = { ...DEFAULT_PROFILE };
  try {
    const raw = localStorage.getItem(LS_KEY);
    if (raw) p = { ...p, ...(JSON.parse(raw) as Partial<Profile>) };
  } catch {
    // ignore
  }
  // 档案里没存头像时，回退到旧 grandumi_avatar，复用原头像选择结果
  if (!p.avatar) {
    try {
      p.avatar = localStorage.getItem(LEGACY_AVATAR_KEY) || "";
    } catch {
      // ignore
    }
  }
  return p;
}

const state = reactive<Profile>(load());

export function useProfile() {
  function setProfile(p: Profile) {
    Object.assign(state, p);
    try {
      localStorage.setItem(LS_KEY, JSON.stringify(state));
      // 同步旧头像键，保持与原图片头像功能一致
      if (state.avatar) localStorage.setItem(LEGACY_AVATAR_KEY, state.avatar);
    } catch {
      // ignore
    }
  }
  return { profile: state, setProfile };
}
