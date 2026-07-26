# Test Configuration Security

## Overview

Test configuration has been hardened to prevent committing credentials to version control.

## Configuration Priority (Highest to Lowest)

1. **Environment Variables** (Recommended for CI/CD)
2. **dotnet user-secrets** (Recommended for local development)
3. **appsettings.Test.local.json** (Gitignored local override)
4. **appsettings.Test.json** (Base configuration, no secrets)

## Setup for Local Development

1. Initialize user-secrets for the API project if needed:
   ```bash
   dotnet user-secrets init --project PointOfSale.API
   ```

2. Set the required local secrets:
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=pointofsale_tests;Username=your_user;Password=your_password;Pooling=false;Timeout=15;Command Timeout=30;Timezone=UTC;Include Error Detail=true;SSL Mode=Disable;Trust Server Certificate=true" --project PointOfSale.API
   dotnet user-secrets set "JwtSettings:Secret" "YourTestJwtSecretKeyMustBeAtLeast32CharactersLong!!" --project PointOfSale.API
   dotnet user-secrets set "SuperAdminJwt:Secret" "YourTestSuperAdminJwtSecretKeyMustBeAtLeast32Chars!!" --project PointOfSale.API
   dotnet user-secrets set "StorefrontJwt:Secret" "YourTestStorefrontJwtSecretKeyMustBeAtLeast32Chars!!" --project PointOfSale.API
   dotnet user-secrets set "CustomerJwt:Secret" "YourTestCustomerJwtSecretKeyMustBeAtLeast32Chars!!" --project PointOfSale.API
   dotnet user-secrets set "AffiliateJwt:Secret" "YourTestAffiliateJwtSecretKeyMustBeAtLeast32Chars!!" --project PointOfSale.API
   dotnet user-secrets set "BaileysService:ApiKey" "your-test-baileys-api-key-here-min-32-chars" --project PointOfSale.API
   dotnet user-secrets set "RabbitMQ:Host" "127.0.0.1" --project PointOfSale.API
   dotnet user-secrets set "RabbitMQ:Username" "guest" --project PointOfSale.API
   dotnet user-secrets set "RabbitMQ:Password" "guest" --project PointOfSale.API
   dotnet user-secrets set "MongoDB:ConnectionString" "mongodb://127.0.0.1:27017" --project PointOfSale.API
   dotnet user-secrets set "CommerceHub:OrderAccessSigningKey" "YourTestOrderAccessSigningKeyMustBeAtLeast32Chars!!" --project PointOfSale.API
   dotnet user-secrets set "AbandonedCart:TokenSecret" "YourTestAbandonedCartTokenSecretMustBeAtLeast32Chars!!" --project PointOfSale.API
   dotnet user-secrets set "AbandonedCart:N8nWebhookUrl" "http://127.0.0.1:5678/webhook/abandoned-cart" --project PointOfSale.API
   ```

3. If you prefer a file-based override, copy the example file:
   ```bash
   cp appsettings.Test.local.json.example appsettings.Test.local.json
   ```

4. Edit `appsettings.Test.local.json` with your local database credentials and test secrets:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Host=localhost;Port=5432;Database=pointofsale_tests;Username=your_user;Password=your_password;..."
     },
     "JwtSettings": {
       "Secret": "YourTestJwtSecretKeyMustBeAtLeast32CharactersLong!!"
     },
     "SuperAdminJwt": {
       "Secret": "YourTestSuperAdminJwtSecretKeyMustBeAtLeast32Chars!!"
     },
     "StorefrontJwt": {
       "Secret": "YourTestStorefrontJwtSecretKeyMustBeAtLeast32Chars!!"
     },
     "CustomerJwt": {
       "Secret": "YourTestCustomerJwtSecretKeyMustBeAtLeast32Chars!!"
     },
     "AffiliateJwt": {
       "Secret": "YourTestAffiliateJwtSecretKeyMustBeAtLeast32Chars!!"
     },
     "BaileysService": {
       "ApiKey": "your-test-baileys-api-key-here-min-32-chars"
     },
     "RabbitMQ": {
       "Host": "127.0.0.1",
       "VirtualHost": "/",
       "Username": "guest",
       "Password": "guest"
     },
     "MongoDB": {
       "ConnectionString": "mongodb://127.0.0.1:27017",
       "DatabaseName": "pointofsale_tests",
       "ProductsCollectionName": "products"
     },
     "CommerceHub": {
       "OrderAccessSigningKey": "YourTestOrderAccessSigningKeyMustBeAtLeast32Chars!!"
     },
     "AbandonedCart": {
       "TokenSecret": "YourTestAbandonedCartTokenSecretMustBeAtLeast32Chars!!",
       "N8nWebhookUrl": "http://127.0.0.1:5678/webhook/abandoned-cart"
     }
   }
   ```

5. The `.local.json` file is automatically ignored by git.

## Setup for CI/CD

Set the environment variables:
```bash
export ConnectionStrings__DefaultConnection="Host=...;Port=5432;..."
export JwtSettings__Secret="YourCIJwtSecret32CharsMinimum!!"
export SuperAdminJwt__Secret="YourCISuperAdminSecret32CharsMin!!"
export StorefrontJwt__Secret="YourCIStorefrontSecret32CharsMin!!"
export CustomerJwt__Secret="YourCICustomerSecret32CharsMinimum!!"
export AffiliateJwt__Secret="YourCIAffiliateSecret32CharsMin!!"
export BaileysService__ApiKey="your-ci-baileys-key-32-chars-min"
export RabbitMQ__Host="127.0.0.1"
export RabbitMQ__Username="guest"
export RabbitMQ__Password="guest"
export MongoDB__ConnectionString="mongodb://127.0.0.1:27017"
export CommerceHub__OrderAccessSigningKey="YourCIOrderSigningKey32CharsMin!!"
export AbandonedCart__TokenSecret="YourCIAbandonedCartSecret32CharsMin!!"
export AbandonedCart__N8nWebhookUrl="http://127.0.0.1:5678/webhook/abandoned-cart"
```

Or in your CI configuration:
```yaml
env:
  ConnectionStrings__DefaultConnection: ${{ secrets.TEST_DB_CONNECTION }}
  JwtSettings__Secret: ${{ secrets.TEST_JWT_SECRET }}
  SuperAdminJwt__Secret: ${{ secrets.TEST_SUPERADMIN_JWT_SECRET }}
  StorefrontJwt__Secret: ${{ secrets.TEST_STOREFRONT_JWT_SECRET }}
  CustomerJwt__Secret: ${{ secrets.TEST_CUSTOMER_JWT_SECRET }}
  AffiliateJwt__Secret: ${{ secrets.TEST_AFFILIATE_JWT_SECRET }}
  BaileysService__ApiKey: ${{ secrets.TEST_BAILEYS_API_KEY }}
  RabbitMQ__Host: ${{ secrets.TEST_RABBITMQ_HOST }}
  RabbitMQ__Username: ${{ secrets.TEST_RABBITMQ_USERNAME }}
  RabbitMQ__Password: ${{ secrets.TEST_RABBITMQ_PASSWORD }}
  MongoDB__ConnectionString: ${{ secrets.TEST_MONGO_CONNECTION_STRING }}
  CommerceHub__OrderAccessSigningKey: ${{ secrets.TEST_ORDER_SIGNING_KEY }}
  AbandonedCart__TokenSecret: ${{ secrets.TEST_ABANDONED_CART_SECRET }}
  AbandonedCart__N8nWebhookUrl: ${{ secrets.TEST_ABANDONED_CART_WEBHOOK_URL }}
```

## Running Tests

Tests will automatically use the configuration in this priority order:
1. Environment variable `ConnectionStrings__DefaultConnection`
2. `dotnet user-secrets`
3. `appsettings.Test.local.json` (if exists)
4. `appsettings.Test.json` (will fail if secrets are still missing)

## Security Notes

- Never commit `appsettings.Test.local.json` to version control
- Prefer `dotnet user-secrets` over local JSON for developer machines
- Never commit credentials or real secrets in `appsettings.Test.json`
- `appsettings.Test.json` intentionally contains blank secrets and will fail fast until secure values are provided
- Use environment variables in CI/CD pipelines
- The example file shows the expected format without real credentials
- All JWT secrets must be at least 32 characters long
- Generate unique secrets for each environment (dev, test, staging, production)
