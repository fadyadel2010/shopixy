-- Check the service request status and review status
SELECT 
    sr."Id" as "RequestId",
    sr."Status",
    sr."BuyerUserId",
    sr."ProviderUserId",
    sr."CompletedByProviderAt",
    sr."CompletedByBuyerAt",
    srv."Id" as "ReviewId",
    srv."Rating"
FROM "ServiceRequests" sr
LEFT JOIN "ServiceReviews" srv ON srv."ServiceRequestId" = sr."Id"
WHERE sr."IsDeleted" = false
ORDER BY sr."CreatedAt" DESC
LIMIT 10;
