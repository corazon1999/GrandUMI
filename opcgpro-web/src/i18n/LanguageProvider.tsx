"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import {
  DEFAULT_LOCALE,
  isSupportedLocale,
  translateText,
} from "./core.mjs";

export type Locale = "zh-CN" | "ja" | "en";

interface LanguageContextValue {
  locale: Locale;
  setLocale: (locale: Locale) => void;
  t: (text: string) => string;
}

interface RenderedValue {
  source: string;
  rendered: string;
}

const STORAGE_KEY = "grandumi_language";
const TRANSLATED_ATTRIBUTES = ["aria-label", "placeholder", "title"] as const;
const LanguageContext = createContext<LanguageContextValue | null>(null);

export function useLanguage(): LanguageContextValue {
  const value = useContext(LanguageContext);
  if (!value) throw new Error("useLanguage must be used inside LanguageProvider");
  return value;
}

function shouldSkip(node: Node): boolean {
  const element = node instanceof Element ? node : node.parentElement;
  return Boolean(element?.closest("[data-no-i18n]"));
}

export default function LanguageProvider({ children }: { children: ReactNode }) {
  const [locale, setLocaleState] = useState<Locale>(DEFAULT_LOCALE);
  const localeRef = useRef<Locale>(DEFAULT_LOCALE);
  const textValues = useRef(new WeakMap<Text, RenderedValue>());
  const attributeValues = useRef(new WeakMap<Element, Map<string, RenderedValue>>());

  const translateTextNode = useCallback((node: Text) => {
    if (shouldSkip(node)) return;
    const current = node.nodeValue ?? "";
    const previous = textValues.current.get(node);
    const source = previous && current === previous.rendered ? previous.source : current;
    const rendered = translateText(source, localeRef.current);
    textValues.current.set(node, { source, rendered });
    if (current !== rendered) node.nodeValue = rendered;
  }, []);

  const translateElement = useCallback((element: Element) => {
    if (shouldSkip(element)) return;
    let values = attributeValues.current.get(element);
    if (!values) {
      values = new Map();
      attributeValues.current.set(element, values);
    }

    for (const attribute of TRANSLATED_ATTRIBUTES) {
      const current = element.getAttribute(attribute);
      if (current === null) continue;
      const previous = values.get(attribute);
      const source = previous && current === previous.rendered ? previous.source : current;
      const rendered = translateText(source, localeRef.current);
      values.set(attribute, { source, rendered });
      if (current !== rendered) element.setAttribute(attribute, rendered);
    }
  }, []);

  const translateTree = useCallback((root: Node) => {
    if (root.nodeType === Node.TEXT_NODE) {
      translateTextNode(root as Text);
      return;
    }
    if (!(root instanceof Element) || shouldSkip(root)) return;
    translateElement(root);
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
    let node = walker.nextNode();
    while (node) {
      if (node.nodeType === Node.TEXT_NODE) translateTextNode(node as Text);
      else translateElement(node as Element);
      node = walker.nextNode();
    }
  }, [translateElement, translateTextNode]);

  useEffect(() => {
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      if (isSupportedLocale(saved)) setLocaleState(saved as Locale);
    } catch {
      // Storage is optional; the in-memory language switch still works.
    }
  }, []);

  useEffect(() => {
    localeRef.current = locale;
    document.documentElement.lang = locale;
    document.documentElement.dataset.locale = locale;
    translateTree(document.body);
  }, [locale, translateTree]);

  useEffect(() => {
    const observer = new MutationObserver((mutations) => {
      for (const mutation of mutations) {
        if (mutation.type === "characterData") {
          translateTextNode(mutation.target as Text);
        } else if (mutation.type === "attributes") {
          translateElement(mutation.target as Element);
        } else {
          mutation.addedNodes.forEach(translateTree);
        }
      }
    });
    observer.observe(document.body, {
      subtree: true,
      childList: true,
      characterData: true,
      attributes: true,
      attributeFilter: [...TRANSLATED_ATTRIBUTES],
    });
    return () => observer.disconnect();
  }, [translateElement, translateTextNode, translateTree]);

  const setLocale = useCallback((next: Locale) => {
    setLocaleState(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // Storage is optional; the in-memory language switch still works.
    }
  }, []);

  const t = useCallback((text: string) => translateText(text, locale), [locale]);
  const value = useMemo(() => ({ locale, setLocale, t }), [locale, setLocale, t]);

  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>;
}
