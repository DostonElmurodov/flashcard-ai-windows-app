import { useLayoutEffect, useRef, type ReactNode } from 'react';

export default function WordCardGrid({ children }: { children: ReactNode }) {
 const ref = useRef<HTMLDivElement>(null);
 useLayoutEffect(() => {
  const grid = ref.current;
  if (!grid) return;
  const cards = Array.from(grid.children) as HTMLElement[];
  // One-pixel tracks let each card reserve only its own height plus the gap.
  // Observing cards also handles expanded details, wrapping and font changes.
  const measure = () => {
   for (const card of cards) {
    const span = `span ${Math.ceil(card.getBoundingClientRect().height) + 12}`;
    if (card.style.gridRowEnd !== span) card.style.gridRowEnd = span;
   }
  };
  measure();
  const observer = new ResizeObserver(measure);
  cards.forEach(card => observer.observe(card));
  return () => observer.disconnect();
 }, [children]);
 return <div ref={ref} className="word-list word-card-grid">{children}</div>;
}
