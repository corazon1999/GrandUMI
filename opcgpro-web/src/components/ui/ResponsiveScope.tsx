"use client";

import { createContext, useContext } from "react";

const ContainerResponsiveContext = createContext(false);
const LayoutQuarterTurnContext = createContext(false);

export function ContainerResponsiveProvider({ children }: { children: React.ReactNode }) {
  return (
    <ContainerResponsiveContext.Provider value>
      {children}
    </ContainerResponsiveContext.Provider>
  );
}

export function useContainerResponsive() {
  return useContext(ContainerResponsiveContext);
}

export function LayoutQuarterTurnProvider({
  rotateQuarterTurn,
  children,
}: {
  rotateQuarterTurn: boolean;
  children: React.ReactNode;
}) {
  return (
    <LayoutQuarterTurnContext.Provider value={rotateQuarterTurn}>
      {children}
    </LayoutQuarterTurnContext.Provider>
  );
}

export function useLayoutQuarterTurn() {
  return useContext(LayoutQuarterTurnContext);
}
