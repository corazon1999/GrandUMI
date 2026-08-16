/**
 * 检查已知的「启动主要」前置成本是否可支付。
 * 服务端仍会做最终权威校验；此处用于避免客户端展示无法完成的发动操作。
 */
export function canPayActivatedMainCost(
  cardNumber: string | null,
  isTapped: boolean,
  cannotBeRested: boolean,
): boolean {
  if (cardNumber === "OP17-044") return !isTapped && !cannotBeRested;
  return true;
}
