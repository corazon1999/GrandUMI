"use client";

import { useEffect, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { useNet } from "@/hooks/useNet";
import { useNetStore } from "@/store/netStore";
import MessageBox from "@/components/ui/MessageBox";

export default function NetProvider({ children }: { children: ReactNode }) {
  useNet();
  const router = useRouter();
  const navigateTo = useNetStore((s) => s.navigateTo);

  useEffect(() => {
    if (navigateTo) {
      useNetStore.getState().setNavigateTo(null);
      router.push(navigateTo);
    }
  }, [navigateTo, router]);

  return (
    <>
      {children}
      <MessageBox />
    </>
  );
}
