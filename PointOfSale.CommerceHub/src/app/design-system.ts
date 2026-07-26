import { brand } from "@/shared/brand";

export const designSystem = {
  brand,
  colors: {
    primary: "hsl(227 92% 60%)",
    secondary: "hsl(256 100% 97%)",
    accent: "hsl(320 84% 62%)",
    background: "hsl(222 45% 98%)",
    muted: "hsl(222 30% 94%)",
    foreground: "hsl(228 30% 14%)",
    success: "hsl(154 63% 40%)",
    warning: "hsl(36 92% 54%)"
  },
  gradients: {
    primary: "linear-gradient(135deg, hsl(227 92% 60%), hsl(320 84% 62%))",
    soft: "linear-gradient(180deg, rgba(255,255,255,0.98), rgba(244,246,255,0.9))"
  },
  spacing: {
    xs: "0.25rem",
    sm: "0.5rem",
    md: "1rem",
    lg: "1.5rem",
    xl: "2rem",
    "2xl": "3rem"
  },
  radii: {
    lg: "1.25rem",
    xl: "1.5rem",
    "2xl": "1.75rem",
    "3xl": "2rem"
  },
  shadows: {
    soft: "0 18px 40px -28px rgba(24, 39, 75, 0.28)",
    card: "0 24px 60px -36px rgba(34, 49, 99, 0.32)",
    glow: "0 24px 54px -28px rgba(103, 76, 255, 0.45)"
  },
  typography: {
    hero: "3.5rem",
    h1: "2.5rem",
    h2: "2rem",
    h3: "1.5rem",
    body: "1rem",
    small: "0.875rem",
    caption: "0.75rem"
  }
} as const;
