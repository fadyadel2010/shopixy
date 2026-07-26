-- PostgreSQL uses xmin system column for optimistic concurrency control
-- No manual column addition needed - xmin is automatically maintained by PostgreSQL
-- EF Core will be configured to use xmin as the concurrency token via shadow property

-- This migration file is kept for historical reference but performs no operations
-- The xmin column is a system column that exists on all PostgreSQL tables by default
