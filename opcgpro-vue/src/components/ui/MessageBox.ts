/**
 * MessageBox —— 命令式消息提示的"内核"
 * （迁移自旧项目 components/ui/MessageBox.tsx 的 showMessage API）
 *
 * 设计：把"发消息"的命令式 API 与"渲染 toast"的 UI 解耦：
 *   - showMessage() 只往 messageBus 发事件，net/协议层可安全调用（无需 Vue 组件上下文）
 *   - Phase 5 会加一个挂载式 Vue 宿主（订阅 messageBus 渲染真正的 toast）
 * 在宿主接入前，先 console 输出，保证链路可用。
 */
import mitt from "mitt";

export type MessageType = "info" | "error" | "success";

export type MessageEvents = {
  show: { text: string; type: MessageType };
};

export const messageBus = mitt<MessageEvents>();

export function showMessage(text: string, type: MessageType = "info"): void {
  messageBus.emit("show", { text, type });
  console.log(`[message:${type}] ${text}`);
}
