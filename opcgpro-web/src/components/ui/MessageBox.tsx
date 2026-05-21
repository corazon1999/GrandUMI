"use client";

import { motion, AnimatePresence } from "framer-motion";
import { useEffect, useState } from "react";

interface Message {
  id: number;
  text: string;
  type: "info" | "warn" | "error";
}

let msgId = 0;
const listeners: ((msg: Message) => void)[] = [];

export function showMessage(
  text: string,
  type: Message["type"] = "info"
): void {
  const msg: Message = { id: ++msgId, text, type };
  listeners.forEach((fn) => fn(msg));
}

export default function MessageBox() {
  const [messages, setMessages] = useState<Message[]>([]);

  useEffect(() => {
    const handler = (msg: Message) => {
      setMessages((prev) => [...prev, msg]);
      setTimeout(() => {
        setMessages((prev) => prev.filter((m) => m.id !== msg.id));
      }, 3000);
    };
    listeners.push(handler);
    return () => {
      const idx = listeners.indexOf(handler);
      if (idx >= 0) listeners.splice(idx, 1);
    };
  }, []);

  const colorMap = {
    info: "bg-gray-800 border-gray-600",
    warn: "bg-yellow-900 border-yellow-600",
    error: "bg-red-900 border-red-600",
  };

  return (
    <div className="fixed top-4 left-1/2 -translate-x-1/2 z-50 flex flex-col gap-2 pointer-events-none">
      <AnimatePresence>
        {messages.map((msg) => (
          <motion.div
            key={msg.id}
            className={`px-4 py-2 rounded-lg border text-white text-sm shadow-lg ${colorMap[msg.type]}`}
            initial={{ opacity: 0, y: -10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -10 }}
          >
            {msg.text}
          </motion.div>
        ))}
      </AnimatePresence>
    </div>
  );
}
