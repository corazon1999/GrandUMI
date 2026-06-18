"use client";

import { motion, AnimatePresence } from "framer-motion";

interface Props {
  open: boolean;
  onClose?: () => void;
  title?: string;
  children: React.ReactNode;
  /** 弹窗最大宽度类名,默认 max-w-2xl */
  maxWidthClass?: string;
}

export default function Modal({ open, onClose, title, children, maxWidthClass = "max-w-2xl" }: Props) {
  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-50 flex items-center justify-center"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
        >
          <div
            className="absolute inset-0 bg-black/60 backdrop-blur-sm"
            onClick={onClose}
          />
          <motion.div
            className={`relative bg-gray-900 border border-gray-700 rounded-xl p-6 min-w-80 ${maxWidthClass} shadow-2xl z-10`}
            initial={{ scale: 0.9, y: 20 }}
            animate={{ scale: 1, y: 0 }}
            exit={{ scale: 0.9, y: 20 }}
          >
            {title && (
              <h2 className="text-lg font-bold text-white mb-4">{title}</h2>
            )}
            {children}
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
