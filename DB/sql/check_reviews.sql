-- Check if reviews exist
SELECT 
    r."Id" as "ReviewId",
    r."Rating",
    r."Comment",
    r."ServiceRequestId",
    sr."ServiceId",
    sr."BuyerUserId",
    sr."ProviderUserId",
    s."Title" as "ServiceTitle",
    r."CreatedAt"
FROM "ServiceReviews" r
INNER JOIN "ServiceRequests" sr ON r."ServiceRequestId" = sr."Id"
INNER JOIN "Services" s ON sr."ServiceId" = s."Id"
WHERE r."IsDeleted" = false
ORDER BY r."CreatedAt" DESC;
