import { useQuery } from "@tanstack/react-query";

import { productDetailQueryOptions } from "./queries";

export const useProductDetail = (id: string) => useQuery(productDetailQueryOptions(id));
