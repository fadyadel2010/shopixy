/** Unadmitted wire data. Authority validators must narrow this before use. */
export type ApiJson = null | boolean | number | string | ApiJson[] | { [key: string]: ApiJson };
