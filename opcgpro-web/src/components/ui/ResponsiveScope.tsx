"use client";

import { createContext, useContext } from "react";

const ContainerResponsiveContext = createContext(false);

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
