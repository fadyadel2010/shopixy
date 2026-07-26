# CommerceHub Refund Upload Strategy

CommerceHub refund evidence now follows the same pattern as POS Admin product media uploads:

1. Browser uploads `multipart/form-data` to the .NET API
2. The API uploads the file to Cloudflare R2
3. The API returns the authenticated media URL used by the refund flow

This avoids browser-to-R2 preflight checks entirely, so refund evidence uploads no longer depend on R2 bucket CORS settings.

## Why Product Uploads Worked

The product page at `http://localhost:5173/products/new/edit` works because the browser never sends the file directly to R2. The file goes to the API first, and the API talks to R2 server-side.

Refund evidence now uses the same approach.

## Current Browser Request Shape

```ts
const formData = new FormData();
formData.append("file", file);
formData.append("sha256", sha256);

await httpClient.post("/api/customers/refunds/upload", formData, {
  headers: {
    "Content-Type": "multipart/form-data"
  }
});
```

## If Direct R2 Uploads Are Reintroduced Later

Direct browser uploads to R2 will require bucket CORS in Cloudflare Dashboard:

`R2 -> <bucket> -> Settings -> CORS Policy`

```json
[
  {
    "AllowedOrigins": ["http://localhost:5175"],
    "AllowedMethods": ["GET", "PUT", "POST"],
    "AllowedHeaders": ["*"],
    "ExposeHeaders": ["ETag"],
    "MaxAgeSeconds": 3600
  }
]
```

Use the exact frontend origin, including the port. In production, replace localhost with deployed app origins and avoid `"*"`.
