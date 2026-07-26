import { brand } from "@/shared/brand";

export const Logo = () => {
  return (
    <div className="flex items-center gap-3">
      <div className="surface-glow brand-gradient relative flex h-11 w-11 items-center justify-center rounded-[1.35rem] text-white">
        <div className="absolute inset-[1px] rounded-[1.2rem] border border-white/20" aria-hidden="true" />
        <span className="relative font-display text-lg font-bold">S</span>
      </div>
      <div>
        <p className="font-display text-xl font-bold leading-none tracking-tight text-foreground">{brand.name}</p>
        <p className="mt-1 text-[0.68rem] font-semibold uppercase tracking-[0.28em] text-muted-foreground">
          Marketplace
        </p>
      </div>
    </div>
  );
};
