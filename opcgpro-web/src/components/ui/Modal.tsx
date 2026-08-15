"use client";

import { motion, AnimatePresence } from "framer-motion";
import { useEffect, useId, useRef } from "react";
import { useContainerResponsive, useLayoutQuarterTurn } from "./ResponsiveScope";

interface Props {
  open: boolean;
  onClose?: () => void;
  title?: string;
  children: React.ReactNode;
  /** 弹窗最大宽度类名,默认 max-w-2xl */
  maxWidthClass?: string;
  /** 移动端使用底部抽屉，lg 及以上仍保持居中弹窗。 */
  mobileSheet?: boolean;
}

export default function Modal({
  open,
  onClose,
  title,
  children,
  maxWidthClass = "max-w-2xl",
  mobileSheet = false,
}: Props) {
  const titleId = useId();
  const dialogRef = useRef<HTMLDivElement>(null);
  const onCloseRef = useRef(onClose);
  const containerResponsive = useContainerResponsive();
  const rotateQuarterTurn = useLayoutQuarterTurn();
  const useMobileSheet = mobileSheet && !rotateQuarterTurn;
  const largeSheetClasses = containerResponsive
    ? "@[1024px]:items-center @[1024px]:justify-center @[1024px]:p-4"
    : "lg:items-center lg:justify-center lg:p-4";
  const largeDialogClasses = containerResponsive
    ? "@[1024px]:w-[calc(100cqw-2rem)] @[1024px]:rounded-xl @[1024px]:border-b @[1024px]:p-6"
    : "lg:w-[calc(100vw-2rem)] lg:rounded-xl lg:border-b lg:p-6";
  const mediumDialogPadding = containerResponsive ? "@[640px]:p-6" : "sm:p-6";
  const dialogWidthClass = containerResponsive ? "w-[calc(100cqw-2rem)]" : "w-[calc(100vw-2rem)]";
  const maxHeightClass = containerResponsive
    ? "max-h-[calc(100cqh-2rem-var(--layout-safe-top,0px)-var(--layout-safe-bottom,0px))]"
    : "max-h-[calc(100dvh-2rem-env(safe-area-inset-top)-env(safe-area-inset-bottom))]";

  useEffect(() => {
    onCloseRef.current = onClose;
  }, [onClose]);

  useEffect(() => {
    if (!open) return;

    const previousFocus = document.activeElement as HTMLElement | null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";

    const dialog = dialogRef.current;
    requestAnimationFrame(() => dialog?.focus());

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && onCloseRef.current) {
        event.preventDefault();
        onCloseRef.current();
        return;
      }
      if (event.key !== "Tab" || !dialog) return;

      const focusable = Array.from(
        dialog.querySelectorAll<HTMLElement>(
          'button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
        ),
      );
      if (focusable.length === 0) {
        event.preventDefault();
        dialog.focus();
        return;
      }

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      document.body.style.overflow = previousOverflow;
      previousFocus?.focus();
    };
  }, [open]);

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className={`fixed inset-0 z-50 flex ${
            useMobileSheet
              ? `items-end px-0 pb-0 pt-[calc(1rem+var(--layout-safe-top,env(safe-area-inset-top)))] ${largeSheetClasses}`
              : "items-center justify-center px-[calc(1rem+var(--layout-safe-left,env(safe-area-inset-left)))] py-[calc(1rem+var(--layout-safe-top,env(safe-area-inset-top)))] [padding-bottom:calc(1rem+var(--layout-safe-bottom,env(safe-area-inset-bottom)))] [padding-right:calc(1rem+var(--layout-safe-right,env(safe-area-inset-right)))]"
          }`}
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
        >
          <div
            className="absolute inset-0 bg-black/60 backdrop-blur-sm"
            onClick={onClose}
          />
          <motion.div
            ref={dialogRef}
            role="dialog"
            aria-modal="true"
            aria-labelledby={title ? titleId : undefined}
            data-modal-dialog
            tabIndex={-1}
            className={`relative z-10 flex ${maxHeightClass} flex-col overflow-hidden border border-gray-700 bg-gray-900 shadow-2xl outline-none ${
              useMobileSheet
                ? `w-full rounded-t-2xl border-b-0 p-4 pb-[calc(1rem+var(--layout-safe-bottom,env(safe-area-inset-bottom)))] ${largeDialogClasses}`
                : `${dialogWidthClass} rounded-xl p-4 ${mediumDialogPadding}`
            } ${maxWidthClass}`}
            initial={{ scale: 0.9, y: 20 }}
            animate={{ scale: 1, y: 0 }}
            exit={{ scale: 0.9, y: 20 }}
          >
            {title && (
              <h2 id={titleId} className="mb-4 shrink-0 pr-12 text-lg font-bold text-white">
                {title}
              </h2>
            )}
            {onClose && (
              <button
                type="button"
                onClick={onClose}
                aria-label="关闭弹窗"
                className="absolute right-2 top-2 flex h-12 w-12 items-center justify-center rounded-lg text-xl text-gray-400 transition-colors hover:bg-gray-800 hover:text-white focus-visible:outline-2 focus-visible:outline-orange-400"
              >
                ×
              </button>
            )}
            <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain pr-1" data-modal-scroll-region>
              {children}
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
