const SEARCH_SESSION_STORAGE_KEY = "commercehub_search_session_id";

export const getSearchSessionId = () => {
  if (typeof window === "undefined") {
    return "";
  }

  const existingId = window.localStorage.getItem(SEARCH_SESSION_STORAGE_KEY);
  if (existingId) {
    return existingId;
  }

  const nextId = window.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
  window.localStorage.setItem(SEARCH_SESSION_STORAGE_KEY, nextId);
  return nextId;
};
