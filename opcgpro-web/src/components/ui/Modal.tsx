"use client";

import { motion, AnimatePresence } from "framer-motion";
import { useEffect, useId, useRef } from "react";
import { useContainerResponsive } from "./ResponsiveScope";

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
  const largeSheetClasses = containerResponsive
    ? "@[1024px]:items-center @[1024px]:justify-center @[1024px]:p-4"
    : "lg:items-center lg:justify-center lg:p-4";
  const largeDialogClasses = containerResponsive
    ? "@[1024px]:w-[calc(100cqw-2rem)] @[1024px]:rounded-xl @[1024px]:border-b @[1024px]:p-6"
    : "lg:w-[calc(100vw-2rem)] lg:rounded-xl lg:border-b lg:p-6";
  const mediumDialogPadding = containerResponsive ? "@[640px]:p-6" : "sm:p-6";
  const maxHeightClass = containerResponsive ? "max-h-[calc(100cqh-2rem)]" : "max-h-[calc(100dvh-2rem)]";

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
          className={`fixed inset-0 z-50 flex p-4 ${
            mobileSheet ? `items-end px-0 pb-0 ${largeSheetClasses}` : "items-center justify-center"
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
            tabIndex={-1}
            className={`relative z-10 ${maxHeightClass} overflow-hidden border border-gray-700 bg-gray-900 shadow-2xl outline-none ${
              mobileSheet
                ? `w-full rounded-t-2xl border-b-0 p-4 pb-[calc(1rem+env(safe-area-inset-bottom))] ${largeDialogClasses}`
                : `w-[calc(100vw-2rem)] rounded-xl p-4 ${mediumDialogPadding}`
            } ${maxWidthClass}`}
            initial={{ scale: 0.9, y: 20 }}
            animate={{ scale: 1, y: 0 }}
            exit={{ scale: 0.9, y: 20 }}
          >
            {title && (
              <h2 id={titleId} className="mb-4 pr-12 text-lg font-bold text-white">
                {title}
              </h2>
            )}
            {onClose && (
              <button
                type="button"
                onClick={onClose}
                aria-label="关闭弹窗"
                className="absolute right-2 top-2 flex h-11 w-11 items-center justify-center rounded-lg text-xl text-gray-400 transition-colors hover:bg-gray-800 hover:text-white focus-visible:outline-2 focus-visible:outline-orange-400"
              >
                ×
              </button>
            )}
            {children}
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
