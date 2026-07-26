# E-commerce System Architecture

## 🏗️ System Overview

This document outlines the comprehensive architecture for integrating an E-commerce system with the existing POS (Point of Sale) system. The architecture follows microservices principles with polyglot persistence, using the right database for each use case.

---

## 📊 Database Strategy

### PostgreSQL (Source of Truth)
**Purpose**: Transactional data, business logic, financial records

| Data Type | Tables |
|-----------|--------|
| Products | Products, ProductUnits, ProductVariants, Categories |
| Inventory | WarehouseStock, StockMovements, StockTransfers |
| Financial | SalesInvoices, PurchaseInvoices, Payments, AccountLedger |
| Customers | Customers, CustomerTransactions |
| Suppliers | Suppliers, SupplierTransactions |
| Users | Users, AuditLogs |
| Settings | SystemSettings, Banks, Cashboxes |

### MongoDB (E-commerce Read Store)
**Purpose**: Fast reads, flexible schema, horizontal scalability

| Collection | Purpose | Document Structure |
|------------|---------|-------------------|
| `products` | Storefront catalog | Denormalized with embedded categories, images, variants |
| `shopping_carts` | Active shopping sessions | Customer ID, items, prices, timestamps |
| `product_reviews` | Customer reviews & ratings | Product ID, customer, rating, review, helpful votes |
| `wishlists` | Customer wish lists | Customer ID, products, date added |
| `browsing_history` | Customer preferences | Customer ID, viewed products, timestamps |
| `cms_content` | Pages, blogs, banners | Content type, title, body, SEO fields |
| `theme_settings` | E-commerce configuration | Colors, fonts, layouts, logos |
| `search_facets` | Cached filter options | Category facets, brand lists, price ranges |

### Redis (Cache & Real-Time)
**Purpose**: Caching, sessions, real-time counters, distributed locks

| Key Pattern | Purpose | TTL |
|-------------|---------|-----|
| `product:{id}` | Product cache | 5-15 min |
| `category:{id}` | Category cache | 30 min |
| `categories:all` | Category tree | 30 min |
| `inventory:{productId}:{warehouseId}` | Stock levels | 1-2 min |
| `cart:{sessionId}` | Shopping cart | 24 hours |
| `session:{userId}` | User session | 30 min |
| `bestsellers:daily` | Top products | 1 hour |
| `theme:settings` | Theme config | 1 hour |
| `lock:checkout:{orderId}` | Distributed lock | 30 sec |
| `ratelimit:{ip}` | API rate limiting | 1 min |

### Elasticsearch (Search & Analytics)
**Purpose**: Full-text search, faceted filtering, analytics

| Index | Purpose | Sync Frequency |
|-------|---------|----------------|
| `products` | Product search | Real-time + 15 min batch |
| `orders` | Order analytics | Daily batch |
| `search_logs` | Search analytics | Real-time |
| `inventory_reports` | Stock analytics | Hourly |

---

## 🔄 Data Synchronization Patterns

### Pattern 1: Event-Driven Sync (Recommended)

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   POS Admin     │───▶│   PostgreSQL    │───▶│   Event Bus     │
│   Creates/      │    │   (Source of    │    │   (RabbitMQ/    │
│   Updates       │    │    Truth)       │    │    Kafka)       │
│   Product       │    │                 │    │                 │
└─────────────────┘    └─────────────────┘    └────────┬────────┘
                                                       │
                       ┌───────────────────────────────┼───────────────────────────────┐
                       │                               │                               │
                       ▼                               ▼                               ▼
              ┌─────────────────┐             ┌─────────────────┐             ┌─────────────────┐
              │   MongoDB       │             │   Elasticsearch │             │   Redis         │
              │   Sync Handler  │             │   Sync Handler  │             │   Cache Handler │
              │                 │             │                 │             │                 │
              │ - Upsert doc    │             │ - Index/update  │             │ - Invalidate    │
              │ - Denormalize   │             │ - Reindex       │             │ - Warm cache    │
              └─────────────────┘             └─────────────────┘             └─────────────────┘
```

### Event Types

```csharp
// Product Events
ProductCreatedEvent { ProductId, Name, SKU, Price, CategoryId, ... }
ProductUpdatedEvent { ProductId, ChangedFields[], ... }
ProductDeletedEvent { ProductId }
ProductPriceChangedEvent { ProductId, OldPrice, NewPrice }
ProductStockChangedEvent { ProductId, WarehouseId, OldQty, NewQty }

// Order Events
OrderCreatedEvent { OrderId, CustomerId, Items[], Total, ... }
OrderStatusChangedEvent { OrderId, OldStatus, NewStatus }
OrderPaidEvent { OrderId, PaymentMethod, Amount }
OrderShippedEvent { OrderId, TrackingNumber, Carrier }

// Customer Events
CustomerRegisteredEvent { CustomerId, Email, Name }
CustomerUpdatedEvent { CustomerId, ChangedFields[] }

// Inventory Events
StockAdjustedEvent { ProductId, WarehouseId, Adjustment, Reason }
StockTransferredEvent { ProductId, FromWarehouse, ToWarehouse, Quantity }
LowStockAlertEvent { ProductId, CurrentStock, Threshold }
```

---

## 🏛️ Microservices Architecture

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              API Gateway (YARP/Ocelot)                          │
│                     • Rate Limiting  • Load Balancing  • SSL/TLS               │
│                     • Request Routing  • Auth Token Validation                 │
└─────────────────────────────────────────────────────────────────────────────────┘
                                          │
            ┌─────────────────────────────┼─────────────────────────────┐
            │                             │                             │
            ▼                             ▼                             ▼
┌───────────────────────┐   ┌───────────────────────┐   ┌───────────────────────┐
│    POS Service        │   │   E-commerce Service  │   │   Amazon Sync Service │
│    (Port 7000)        │   │   (Port 8000)         │   │   (Background)        │
├───────────────────────┤   ├───────────────────────┤   ├───────────────────────┤
│ • Products CRUD       │   │ • Storefront API      │   │ • SP-API Integration  │
│ • Inventory Mgmt      │   │ • Shopping Cart       │   │ • Product Sync        │
│ • Sales/Purchases     │   │ • Checkout            │   │ • Inventory Sync      │
│ • Financial Reports   │   │ • Product Search      │   │ • Order Import        │
│ • Dashboard           │   │ • Customer Auth       │   │ • Listing Management  │
│ • User Management     │   │ • Order Management    │   │                       │
└───────────┬───────────┘   └───────────┬───────────┘   └───────────┬───────────┘
            │                           │                           │
            │               ┌───────────▼───────────┐              │
            │               │   Shared Services     │              │
            │               ├───────────────────────┤              │
            │               │ • Identity Service    │              │
            └───────────────│ • Notification Svc    │──────────────┘
                            │ • File Storage Svc    │
                            │ • Logging Service     │
                            └───────────────────────┘
                                        │
        ┌───────────────────────────────┼───────────────────────────────┐
        │                               │                               │
        ▼                               ▼                               ▼
┌───────────────────┐       ┌───────────────────┐       ┌───────────────────┐
│    PostgreSQL     │       │     MongoDB       │       │      Redis        │
│  (Primary Store)  │       │  (Read Store)     │       │    (Cache)        │
├───────────────────┤       ├───────────────────┤       ├───────────────────┤
│ Products          │──────▶│ products          │       │ product:{id}      │
│ Inventory         │       │ shopping_carts    │       │ cart:{session}    │
│ Orders            │──────▶│ product_reviews   │       │ session:{user}    │
│ Customers         │       │ wishlists         │       │ inventory:{id}    │
│ Financial         │       │ cms_content       │       │                   │
└───────────────────┘       └───────────────────┘       └───────────────────┘
                                        │
                            ┌───────────▼───────────┐
                            │    Elasticsearch      │
                            ├───────────────────────┤
                            │ products (search)     │
                            │ orders (analytics)    │
                            │ search_logs           │
                            └───────────────────────┘
```

---

## 📂 Project Structure

```
PointOfSale/
├── src/
│   ├── Services/
│   │   ├── POS.API/                          # Existing POS API (PostgreSQL)
│   │   │   ├── Controllers/
│   │   │   ├── Services/
│   │   │   └── DTOs/
│   │   │
│   │   ├── Ecommerce.API/                    # New E-commerce API (MongoDB + Redis)
│   │   │   ├── Controllers/
│   │   │   │   ├── StorefrontController.cs
│   │   │   │   ├── CartController.cs
│   │   │   │   ├── CheckoutController.cs
│   │   │   │   ├── SearchController.cs
│   │   │   │   ├── CustomerController.cs
│   │   │   │   └── OrderController.cs
│   │   │   ├── Services/
│   │   │   │   ├── CatalogService.cs
│   │   │   │   ├── CartService.cs
│   │   │   │   ├── SearchService.cs
│   │   │   │   └── OrderService.cs
│   │   │   └── Models/
│   │   │       ├── ProductDocument.cs
│   │   │       ├── CartDocument.cs
│   │   │       └── ReviewDocument.cs
│   │   │
│   │   ├── AmazonSync.Worker/                # Amazon SP-API Background Service
│   │   │   ├── Jobs/
│   │   │   │   ├── ProductSyncJob.cs
│   │   │   │   ├── InventorySyncJob.cs
│   │   │   │   └── OrderImportJob.cs
│   │   │   └── Clients/
│   │   │       └── AmazonSpApiClient.cs
│   │   │
│   │   ├── Gateway.API/                      # API Gateway (YARP)
│   │   │   ├── yarp.json
│   │   │   └── Program.cs
│   │   │
│   │   └── Shared/
│   │       ├── Identity.API/                 # Shared Auth Service
│   │       ├── Notifications.API/            # Email/SMS Service
│   │       └── FileStorage.API/              # S3/Azure Blob Storage
│   │
│   ├── Core/
│   │   ├── PointOfSale.Core/                 # Existing domain entities
│   │   ├── Ecommerce.Core/                   # E-commerce domain
│   │   │   ├── Entities/
│   │   │   │   ├── Order.cs
│   │   │   │   ├── OrderItem.cs
│   │   │   │   ├── ShippingMethod.cs
│   │   │   │   └── Coupon.cs
│   │   │   └── Events/
│   │   │       ├── ProductEvents.cs
│   │   │       ├── OrderEvents.cs
│   │   │       └── InventoryEvents.cs
│   │   │
│   │   └── Shared.Core/                      # Shared domain logic
│   │       ├── Interfaces/
│   │       └── ValueObjects/
│   │
│   ├── Infrastructure/
│   │   ├── PointOfSale.Infrastructure/       # Existing (PostgreSQL)
│   │   ├── Ecommerce.Infrastructure/         # New (MongoDB + Redis + ES)
│   │   │   ├── MongoDB/
│   │   │   │   ├── MongoDbContext.cs
│   │   │   │   └── Repositories/
│   │   │   ├── Redis/
│   │   │   │   ├── RedisCacheService.cs
│   │   │   │   └── RedisLockService.cs
│   │   │   ├── Elasticsearch/
│   │   │   │   ├── ElasticsearchService.cs
│   │   │   │   └── Indexes/
│   │   │   └── MessageBus/
│   │   │       ├── RabbitMqPublisher.cs
│   │   │       └── EventHandlers/
│   │   │
│   │   └── Shared.Infrastructure/
│   │       ├── Storage/
│   │       └── Email/
│   │
│   └── Web/
│       ├── PointOfSale.Web/                  # Existing POS frontend (React)
│       └── Ecommerce.Web/                    # New E-commerce frontend (Next.js)
│           ├── pages/
│           ├── components/
│           └── styles/
│
├── docker/
│   ├── docker-compose.yml
│   ├── docker-compose.dev.yml
│   └── Dockerfiles/
│
├── tests/
│   ├── POS.Tests/
│   ├── Ecommerce.Tests/
│   └── Integration.Tests/
│
└── docs/
    ├── ECOMMERCE-ARCHITECTURE.md
    └── API-DOCUMENTATION.md
```

---

## 🔧 Technology Stack

### Backend Services

| Technology | Version | Purpose |
|------------|---------|---------|
| ASP.NET Core | 8.0 LTS | API Framework |
| Entity Framework Core | 8.0 | PostgreSQL ORM |
| MongoDB.Driver | 2.x | MongoDB client |
| StackExchange.Redis | 2.x | Redis client |
| NEST | 8.x | Elasticsearch client |
| MassTransit | 8.x | Message bus abstraction |
| RabbitMQ | 3.x | Message broker |
| Hangfire | 1.8.x | Background jobs |
| YARP | 2.x | API Gateway |
| Polly | 8.x | Resilience policies |
| FluentValidation | 11.x | Request validation |
| AutoMapper | 12.x | Object mapping |
| Serilog | 3.x | Structured logging |

### Frontend

| Technology | Version | Purpose |
|------------|---------|---------|
| React | 18.x | POS Admin SPA |
| Next.js | 14.x | E-commerce Storefront |
| Tailwind CSS | 3.x | Styling |
| Redux Toolkit | 2.x | State management |
| Axios | 1.x | HTTP client |
| SignalR | Latest | Real-time updates |

### Infrastructure

| Technology | Purpose |
|------------|---------|
| Docker | Containerization |
| Docker Compose | Local development |
| Kubernetes | Production orchestration |
| Azure/AWS | Cloud hosting |
| GitHub Actions | CI/CD |
| Application Insights | Monitoring |
| ELK Stack | Logging |

---

## 📋 MongoDB Document Schemas

### Product Document

```javascript
{
  "_id": ObjectId("..."),
  "postgresId": "guid-from-postgres",
  "sku": "PROD-001",
  "slug": "blue-widget-xl",
  "name": {
    "en": "Blue Widget XL",
    "ar": "الأداة الزرقاء XL"
  },
  "description": {
    "en": "A high-quality blue widget...",
    "ar": "أداة زرقاء عالية الجودة..."
  },
  "seo": {
    "metaTitle": { "en": "...", "ar": "..." },
    "metaDescription": { "en": "...", "ar": "..." },
    "keywords": ["widget", "blue", "xl"]
  },
  "category": {
    "_id": ObjectId("..."),
    "postgresId": "guid",
    "name": { "en": "Electronics", "ar": "إلكترونيات" },
    "slug": "electronics",
    "breadcrumb": [
      { "name": { "en": "Home", "ar": "الرئيسية" }, "slug": "" },
      { "name": { "en": "Electronics", "ar": "إلكترونيات" }, "slug": "electronics" }
    ]
  },
  "brand": {
    "name": "WidgetCo",
    "logo": "https://..."
  },
  "images": [
    {
      "url": "https://...",
      "alt": { "en": "Widget front view", "ar": "..." },
      "isPrimary": true,
      "sortOrder": 1
    }
  ],
  "pricing": {
    "basePrice": 99.99,
    "salePrice": 79.99,
    "currency": "USD",
    "taxIncluded": false,
    "taxRate": 0.15
  },
  "variants": [
    {
      "sku": "PROD-001-BLU-XL",
      "name": "Blue - XL",
      "attributes": { "color": "Blue", "size": "XL" },
      "priceAdjustment": 0,
      "stock": 50,
      "images": ["https://..."]
    }
  ],
  "inventory": {
    "totalStock": 150,
    "warehouses": [
      { "id": "guid", "name": "Main", "stock": 100 },
      { "id": "guid", "name": "Branch", "stock": 50 }
    ],
    "lowStockThreshold": 10,
    "trackInventory": true,
    "allowBackorder": false
  },
  "shipping": {
    "weight": 0.5,
    "dimensions": { "length": 10, "width": 5, "height": 3 },
    "weightUnit": "kg",
    "dimensionUnit": "cm",
    "freeShipping": false
  },
  "ratings": {
    "average": 4.5,
    "count": 128,
    "distribution": { "5": 80, "4": 30, "3": 10, "2": 5, "1": 3 }
  },
  "status": "published", // draft, published, archived
  "visibility": {
    "online": true,
    "searchable": true,
    "featuredOnHomepage": true
  },
  "tags": ["bestseller", "new-arrival"],
  "relatedProducts": [ObjectId("..."), ObjectId("...")],
  "syncedAt": ISODate("2024-01-15T10:30:00Z"),
  "createdAt": ISODate("2024-01-01T00:00:00Z"),
  "updatedAt": ISODate("2024-01-15T10:30:00Z")
}
```

### Shopping Cart Document

```javascript
{
  "_id": ObjectId("..."),
  "sessionId": "uuid-session-id",
  "customerId": "guid-customer-id", // null for guest
  "items": [
    {
      "productId": ObjectId("..."),
      "postgresProductId": "guid",
      "sku": "PROD-001-BLU-XL",
      "name": "Blue Widget XL",
      "image": "https://...",
      "unitPrice": 79.99,
      "quantity": 2,
      "subtotal": 159.98,
      "addedAt": ISODate("...")
    }
  ],
  "couponCode": "SAVE10",
  "discount": {
    "type": "percentage",
    "value": 10,
    "amount": 15.99
  },
  "subtotal": 159.98,
  "discountTotal": 15.99,
  "taxTotal": 21.60,
  "shippingTotal": 0,
  "grandTotal": 165.59,
  "currency": "USD",
  "metadata": {
    "userAgent": "...",
    "ipAddress": "...",
    "referrer": "..."
  },
  "createdAt": ISODate("..."),
  "updatedAt": ISODate("..."),
  "expiresAt": ISODate("...") // TTL index
}
```

### Product Review Document

```javascript
{
  "_id": ObjectId("..."),
  "productId": ObjectId("..."),
  "postgresProductId": "guid",
  "customerId": "guid",
  "customerName": "John Doe",
  "rating": 5,
  "title": "Great product!",
  "body": "I love this widget...",
  "images": ["https://..."],
  "verified": true, // verified purchase
  "helpful": {
    "yes": 15,
    "no": 2
  },
  "status": "approved", // pending, approved, rejected
  "response": {
    "body": "Thank you for your review!",
    "respondedAt": ISODate("..."),
    "respondedBy": "admin"
  },
  "createdAt": ISODate("..."),
  "updatedAt": ISODate("...")
}
```

---

## 🚀 Implementation Phases

### Phase 0: POS System Preparation (1-2 weeks)
**Goal**: Prepare existing POS system for e-commerce integration

- [ ] Add missing product fields (SKU, SEO, weight, dimensions)
- [ ] Add product publish status (draft/published/archived)
- [ ] Add order entity (separate from SalesInvoice)
- [ ] Set up event publishing infrastructure
- [ ] Enable API versioning
- [ ] Add Swagger documentation

### Phase 1: Infrastructure Setup (1 week)
**Goal**: Set up all required databases and services

- [ ] Set up MongoDB (local + cloud)
- [ ] Set up Redis (local + cloud)
- [ ] Set up Elasticsearch (local + cloud)
- [ ] Set up RabbitMQ
- [ ] Create Docker Compose for development
- [ ] Set up monitoring (Seq/ELK)

### Phase 2: Core Services (2-3 weeks)
**Goal**: Build shared infrastructure services

- [ ] Create message bus abstraction (MassTransit)
- [ ] Create event publishers in POS API
- [ ] Create event handlers for MongoDB sync
- [ ] Create event handlers for Elasticsearch sync
- [ ] Create Redis cache service
- [ ] Create distributed lock service

### Phase 3: E-commerce API (3-4 weeks)
**Goal**: Build e-commerce backend services

- [ ] Catalog Service (products, categories)
- [ ] Search Service (Elasticsearch integration)
- [ ] Cart Service (MongoDB + Redis)
- [ ] Checkout Service (order creation)
- [ ] Customer Service (auth, profile)
- [ ] Order Service (management, tracking)
- [ ] Review Service (ratings, reviews)

### Phase 4: E-commerce Frontend (4-6 weeks)
**Goal**: Build customer-facing storefront

- [ ] Next.js project setup
- [ ] Product listing pages
- [ ] Product detail page
- [ ] Shopping cart
- [ ] Checkout flow
- [ ] Customer account
- [ ] Order history
- [ ] Search functionality

### Phase 5: Amazon Integration (2-3 weeks)
**Goal**: Sync with Amazon marketplace

- [ ] Amazon SP-API client
- [ ] Product sync (POS → Amazon)
- [ ] Inventory sync (real-time)
- [ ] Order import (Amazon → POS)
- [ ] FBA integration

### Phase 6: Admin & Optimization (2 weeks)
**Goal**: Admin tools and performance tuning

- [ ] E-commerce admin dashboard
- [ ] CMS for pages/banners
- [ ] Performance optimization
- [ ] Load testing
- [ ] Security audit

---

## ⚙️ Configuration Examples

### Docker Compose (Development)

```yaml
version: '3.8'

services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_USER: pos_user
      POSTGRES_PASSWORD: pos_password
      POSTGRES_DB: PointOfSaleDB
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data

  mongodb:
    image: mongo:7
    environment:
      MONGO_INITDB_ROOT_USERNAME: mongo_user
      MONGO_INITDB_ROOT_PASSWORD: mongo_password
    ports:
      - "27017:27017"
    volumes:
      - mongodb_data:/data/db

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    command: redis-server --appendonly yes
    volumes:
      - redis_data:/data

  elasticsearch:
    image: docker.elastic.co/elasticsearch/elasticsearch:8.11.0
    environment:
      - discovery.type=single-node
      - xpack.security.enabled=false
      - "ES_JAVA_OPTS=-Xms512m -Xmx512m"
    ports:
      - "9200:9200"
    volumes:
      - elasticsearch_data:/usr/share/elasticsearch/data

  rabbitmq:
    image: rabbitmq:3-management
    ports:
      - "5672:5672"
      - "15672:15672"
    environment:
      RABBITMQ_DEFAULT_USER: rabbit_user
      RABBITMQ_DEFAULT_PASS: rabbit_password

  kibana:
    image: docker.elastic.co/kibana/kibana:8.11.0
    ports:
      - "5601:5601"
    environment:
      ELASTICSEARCH_HOSTS: http://elasticsearch:9200
    depends_on:
      - elasticsearch

volumes:
  postgres_data:
  mongodb_data:
  redis_data:
  elasticsearch_data:
```

### appsettings.json (E-commerce API)

```json
{
  "ConnectionStrings": {
    "MongoDB": "mongodb://mongo_user:mongo_password@localhost:27017/ecommerce?authSource=admin",
    "Redis": "localhost:6379",
    "Elasticsearch": "http://localhost:9200",
    "RabbitMQ": "amqp://rabbit_user:rabbit_password@localhost:5672"
  },
  "MongoDB": {
    "DatabaseName": "ecommerce",
    "ProductsCollection": "products",
    "CartsCollection": "shopping_carts",
    "ReviewsCollection": "product_reviews"
  },
  "Redis": {
    "InstanceName": "ecommerce:",
    "DefaultExpiration": 300
  },
  "Elasticsearch": {
    "ProductsIndex": "products",
    "OrdersIndex": "orders"
  },
  "Cache": {
    "ProductTTL": 900,
    "CategoryTTL": 1800,
    "InventoryTTL": 120
  }
}
```

---

## 📊 Data Sync Strategy

### Real-Time Events (Critical Data)

| Event | Source | Targets | Latency |
|-------|--------|---------|---------|
| ProductPriceChanged | PostgreSQL | MongoDB, Redis, ES | < 1 sec |
| StockUpdated | PostgreSQL | Redis, MongoDB | < 1 sec |
| OrderCreated | MongoDB | PostgreSQL | < 1 sec |
| LowStockAlert | PostgreSQL | Redis (notification) | < 1 sec |

### Batch Sync (Non-Critical Data)

| Data | Frequency | Source → Target |
|------|-----------|-----------------|
| Full product catalog | Every 15 min | PostgreSQL → MongoDB |
| Search index rebuild | Every 15 min | PostgreSQL → ES |
| Analytics aggregation | Every hour | MongoDB → ES |
| Reports | Daily | All → ES |

---

## 🔒 Security Considerations

### API Security
- JWT authentication for all APIs
- API key authentication for service-to-service
- Rate limiting per IP and per user
- Request signature validation for webhooks

### Data Security
- Encryption at rest for all databases
- TLS/SSL for all connections
- PII data masking in logs
- GDPR compliance features

### Infrastructure Security
- Network segmentation with VPC
- Secrets management (Azure Key Vault / AWS Secrets Manager)
- Regular security audits
- Penetration testing

---

## 📈 Monitoring & Observability

### Metrics to Track
- API response times
- Database query performance
- Cache hit/miss ratio
- Message queue depth
- Error rates by service
- Inventory sync latency

### Logging Strategy
- Structured logging with Serilog
- Correlation IDs across services
- Log aggregation with ELK/Seq
- Alert rules for critical errors

### Health Checks
- Each service exposes `/health` endpoint
- Database connection checks
- Message broker connection checks
- Cache availability checks

---

## 📝 Next Steps

1. **Review and approve this architecture**
2. **Start Phase 0**: Prepare POS system for e-commerce
3. **Set up development environment** with Docker Compose
4. **Begin Phase 1**: Infrastructure setup

Would you like to proceed with implementing Phase 0 (POS System Preparation)?
