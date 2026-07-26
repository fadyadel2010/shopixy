import { useCallback, useEffect, useRef } from "react";

type UseInfiniteScrollOptions = {
  enabled: boolean;
  onIntersect: () => void;
  rootMargin?: string;
};

export const useInfiniteScroll = ({
  enabled,
  onIntersect,
  rootMargin = "240px"
}: UseInfiniteScrollOptions) => {
  const observerRef = useRef<IntersectionObserver | null>(null);

  useEffect(() => {
    return () => observerRef.current?.disconnect();
  }, []);

  return useCallback(
    (node: HTMLDivElement | null) => {
      observerRef.current?.disconnect();

      if (!node || !enabled) {
        return;
      }

      observerRef.current = new IntersectionObserver(
        (entries) => {
          if (entries[0]?.isIntersecting) {
            onIntersect();
          }
        },
        { rootMargin }
      );

      observerRef.current.observe(node);
    },
    [enabled, onIntersect, rootMargin]
  );
};
