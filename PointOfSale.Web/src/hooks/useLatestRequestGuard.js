import { useCallback, useEffect, useRef } from "react";

export const getRequestErrorMessage = (error, fallbackMessage) => (
  error?.response?.data?.message
  || error?.response?.data?.error
  || error?.message
  || fallbackMessage
);

export const createLatestRequestGuard = () => {
  let latestRequestId = 0;

  return {
    beginRequest() {
      latestRequestId += 1;
      return latestRequestId;
    },
    isLatestRequest(requestId) {
      return requestId === latestRequestId;
    },
    invalidate() {
      latestRequestId += 1;
    }
  };
};

export const useLatestRequestGuard = () => {
  const guardRef = useRef(null);

  if (guardRef.current === null) {
    guardRef.current = createLatestRequestGuard();
  }

  useEffect(() => (
    () => {
      guardRef.current?.invalidate();
    }
  ), []);

  const beginRequest = useCallback(() => {
    return guardRef.current.beginRequest();
  }, []);

  const isLatestRequest = useCallback((requestId) => guardRef.current.isLatestRequest(requestId), []);

  return {
    beginRequest,
    isLatestRequest
  };
};
