"use client";

import { useState } from "react";
import ChangelogModal from "./ChangelogModal";

/** 使用真实更新日志检查长内容的滚动、触控与关闭行为。 */
export default function ChangelogLayoutVerification() {
  const [open, setOpen] = useState(true);
  return (
    <main className="h-dvh overflow-hidden">
      <ChangelogModal open={open} onClose={() => setOpen(false)} />
    </main>
  );
}
