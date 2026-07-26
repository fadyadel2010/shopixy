import { PropsWithChildren } from "react";

import { CommerceHubCartSync } from "@/cart";
import { CustomerSessionBootstrap } from "@/providers/CustomerSessionBootstrap";
import { QueryProvider } from "@/providers/QueryProvider";
import { ToastViewport } from "@/shared/ToastViewport";

export const AppProviders = ({ children }: PropsWithChildren) => {
  return (
    <QueryProvider>
      <CustomerSessionBootstrap />
      <CommerceHubCartSync />
      {children}
      <ToastViewport />
    </QueryProvider>
  );
};
