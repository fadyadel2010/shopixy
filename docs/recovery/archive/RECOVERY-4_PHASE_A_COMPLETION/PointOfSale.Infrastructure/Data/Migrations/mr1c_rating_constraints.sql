-- Phase MR-1C: Rating Range CHECK Constraint
-- Ensures Rating is always between 1 and 5 at the database level.
-- Defense-in-depth: entity guard and DTO validation already enforce this,
-- but direct SQL inserts could bypass them.

ALTER TABLE "ServiceReviews"
    DROP CONSTRAINT IF EXISTS "CK_ServiceReviews_RatingRange";
ALTER TABLE "ServiceReviews"
    ADD CONSTRAINT "CK_ServiceReviews_RatingRange"
    CHECK ("Rating" BETWEEN 1 AND 5);

-- Comment length constraint (matches [StringLength(5000)] on DTO)
ALTER TABLE "ServiceReviews"
    DROP CONSTRAINT IF EXISTS "CK_ServiceReviews_CommentLength";
ALTER TABLE "ServiceReviews"
    ADD CONSTRAINT "CK_ServiceReviews_CommentLength"
    CHECK ("Comment" IS NULL OR length("Comment") <= 5000);

-- Platform value constraint on MarketingReviewEvidences
ALTER TABLE "MarketingReviewEvidences"
    DROP CONSTRAINT IF EXISTS "CK_MarketingReviewEvidences_Platform";
ALTER TABLE "MarketingReviewEvidences"
    ADD CONSTRAINT "CK_MarketingReviewEvidences_Platform"
    CHECK ("Platform" IN (
        'Facebook', 'Instagram', 'Google', 'TikTok', 'Twitter',
        'LinkedIn', 'YouTube', 'Snapchat', 'Email', 'WhatsApp', 'Other'
    ));

-- CampaignName non-empty constraint
ALTER TABLE "MarketingReviewEvidences"
    DROP CONSTRAINT IF EXISTS "CK_MarketingReviewEvidences_CampaignNameNotEmpty";
ALTER TABLE "MarketingReviewEvidences"
    ADD CONSTRAINT "CK_MarketingReviewEvidences_CampaignNameNotEmpty"
    CHECK (length("CampaignName") > 0);
