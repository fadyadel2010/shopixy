# Phase 0: POS System Preparation for E-commerce

## 📋 Overview

Before starting the E-commerce system implementation, we need to prepare the existing POS system with the necessary database schema changes, API enhancements, and infrastructure setup.

**Duration**: 1-2 weeks  
**Priority**: Critical  
**Dependencies**: None

---

## ✅ Task Checklist

### 1. Database Schema Updates

#### 1.1 Product Table Enhancements
**File**: `PointOfSale.Core/Entities/Product.cs`

```csharp
// New fields to add to Product entity
public string? SKU { get; set; }                    // Unique product identifier
public string? Slug { get; set; }                   // URL-friendly name
public string? MetaTitle { get; set; }              // SEO title
public string? MetaDescription { get; set; }        // SEO description
public decimal Weight { get; set; }                 // Weight for shipping
public decimal Length { get; set; }                 // Package dimensions
public decimal Width { get; set; }
public decimal Height { get; set; }
public string WeightUnit { get; set; } = "kg";      // kg, lb
public string DimensionUnit { get; set; } = "cm";   // cm, in
public string PublishStatus { get; set; } = "draft"; // draft, published, archived
public bool IsOnlineVisible { get; set; } = false;  // Show on e-commerce
public bool IsSearchable { get; set; } = true;      // Include in search
public int LowStockThreshold { get; set; } = 10;    // Alert threshold
```

| Field | Type | Purpose | Required |
|-------|------|---------|----------|
| `SKU` | VARCHAR(50) | Unique product identifier | ✅ Yes |
| `Slug` | VARCHAR(255) | URL-friendly name | ✅ Yes |
| `MetaTitle` | VARCHAR(255) | SEO title | No |
| `MetaDescription` | TEXT | SEO description | No |
| `Weight` | DECIMAL | Shipping weight | ✅ Yes |
| `Length` | DECIMAL | Package length | No |
| `Width` | DECIMAL | Package width | No |
| `Height` | DECIMAL | Package height | No |
| `WeightUnit` | VARCHAR(10) | Weight unit (kg/lb) | No |
| `DimensionUnit` | VARCHAR(10) | Dimension unit (cm/in) | No |
| `PublishStatus` | VARCHAR(20) | Publication status | ✅ Yes |
| `IsOnlineVisible` | BOOLEAN | E-commerce visibility | ✅ Yes |
| `IsSearchable` | BOOLEAN | Include in search | No |
| `LowStockThreshold` | INT | Low stock alert level | No |

#### 1.2 New Tables Required

**Order Entity** (for e-commerce orders, separate from SalesInvoice)
```csharp
// PointOfSale.Core/Entities/Order.cs
public class Order : BaseEntity
{
    public string OrderNumber { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    
    // Guest checkout info
    public string? GuestEmail { get; set; }
    public string? GuestPhone { get; set; }
    public string? GuestName { get; set; }
    
    // Order status
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    
    // Addresses
    public string ShippingAddress { get; set; }
    public string BillingAddress { get; set; }
    public string? ShippingCity { get; set; }
    public string? ShippingState { get; set; }
    public string? ShippingZipCode { get; set; }
    public string? ShippingCountry { get; set; }
    
    // Shipping
    public Guid? ShippingMethodId { get; set; }
    public ShippingMethod? ShippingMethod { get; set; }
    public decimal ShippingCost { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    
    // Financials
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    
    // Coupon
    public string? CouponCode { get; set; }
    public Guid? CouponId { get; set; }
    public Coupon? Coupon { get; set; }
    
    // Payment
    public PaymentMethod PaymentMethod { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime? PaidAt { get; set; }
    
    // Notes
    public string? CustomerNotes { get; set; }
    public string? AdminNotes { get; set; }
    
    // Source
    public string Source { get; set; } = "Ecommerce"; // Ecommerce, Amazon, POS
    public string? SourceOrderId { get; set; } // External order ID
    
    // Items
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    
    // Timestamps
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
}
```

**Order Item Entity**
```csharp
// PointOfSale.Core/Entities/OrderItem.cs
public class OrderItem : BaseEntity
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; }
    
    public Guid ProductId { get; set; }
    public Product Product { get; set; }
    
    public Guid? UnitId { get; set; }
    public Unit? Unit { get; set; }
    
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    
    public string SKU { get; set; }
    public string ProductName { get; set; }
    public string? VariantName { get; set; }
    
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    
    public string? Notes { get; set; }
}
```

**Shipping Method Entity**
```csharp
// PointOfSale.Core/Entities/ShippingMethod.cs
public class ShippingMethod : BaseEntity
{
    public string Name { get; set; }
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    
    public decimal BaseRate { get; set; }
    public decimal PerKgRate { get; set; }
    public decimal? FreeShippingThreshold { get; set; }
    
    public int EstimatedDeliveryDaysMin { get; set; }
    public int EstimatedDeliveryDaysMax { get; set; }
    
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
```

**Coupon Entity**
```csharp
// PointOfSale.Core/Entities/Coupon.cs
public class Coupon : BaseEntity
{
    public string Code { get; set; }
    public string? Description { get; set; }
    
    public DiscountType DiscountType { get; set; } // Percentage, FixedAmount
    public decimal DiscountValue { get; set; }
    
    public decimal? MinOrderAmount { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    
    public int? MaxUsage { get; set; }
    public int CurrentUsage { get; set; } = 0;
    
    public int? MaxUsagePerCustomer { get; set; }
    
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    // Restrictions
    public bool FirstOrderOnly { get; set; } = false;
    public string? ApplicableProductIds { get; set; } // JSON array
    public string? ApplicableCategoryIds { get; set; } // JSON array
}
```

#### 1.3 New Enums

```csharp
// PointOfSale.Core/Enums/OrderStatus.cs
public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Processing = 2,
    Shipped = 3,
    Delivered = 4,
    Completed = 5,
    Cancelled = 6,
    Refunded = 7,
    OnHold = 8
}

// PointOfSale.Core/Enums/PaymentStatus.cs
public enum PaymentStatus
{
    Pending = 0,
    Authorized = 1,
    Paid = 2,
    PartiallyPaid = 3,
    Failed = 4,
    Refunded = 5,
    PartiallyRefunded = 6
}

// PointOfSale.Core/Enums/DiscountType.cs
public enum DiscountType
{
    Percentage = 0,
    FixedAmount = 1
}

// PointOfSale.Core/Enums/PublishStatus.cs
public enum PublishStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2
}
```

---

### 2. API Enhancements

#### 2.1 Enable Swagger Documentation
**File**: `PointOfSale.API/Program.cs`

```csharp
// Add Swagger with full documentation
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "POS API",
        Version = "v1",
        Description = "Point of Sale System API"
    });
    
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    
    // Include XML comments
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);
});
```

#### 2.2 Add API Versioning
```csharp
// NuGet: Asp.Versioning.Mvc
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("X-Api-Version")
    );
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});
```

#### 2.3 Add Rate Limiting
```csharp
// NuGet: Microsoft.AspNetCore.RateLimiting
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User?.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
    
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
```

---

### 3. Event Publishing Infrastructure

#### 3.1 Install NuGet Packages
```xml
<PackageReference Include="MassTransit" Version="8.1.3" />
<PackageReference Include="MassTransit.RabbitMQ" Version="8.1.3" />
```

#### 3.2 Create Event Base Class
```csharp
// PointOfSale.Core/Events/IntegrationEvent.cs
public abstract class IntegrationEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public string EventType => GetType().Name;
}
```

#### 3.3 Create Product Events
```csharp
// PointOfSale.Core/Events/ProductEvents.cs
public class ProductCreatedEvent : IntegrationEvent
{
    public Guid ProductId { get; set; }
    public string Name { get; set; }
    public string SKU { get; set; }
    public decimal Price { get; set; }
    public Guid CategoryId { get; set; }
    public string Barcode { get; set; }
}

public class ProductUpdatedEvent : IntegrationEvent
{
    public Guid ProductId { get; set; }
    public string[] ChangedFields { get; set; }
    // Include all fields that might have changed
    public string Name { get; set; }
    public string SKU { get; set; }
    public decimal? Price { get; set; }
    public Guid? CategoryId { get; set; }
}

public class ProductDeletedEvent : IntegrationEvent
{
    public Guid ProductId { get; set; }
}

public class ProductPriceChangedEvent : IntegrationEvent
{
    public Guid ProductId { get; set; }
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
}

public class ProductStockChangedEvent : IntegrationEvent
{
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public int OldQuantity { get; set; }
    public int NewQuantity { get; set; }
    public string Reason { get; set; }
}
```

#### 3.4 Create Event Publisher Interface
```csharp
// PointOfSale.Core/Interfaces/IEventPublisher.cs
public interface IEventPublisher
{
    Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) 
        where T : IntegrationEvent;
}
```

#### 3.5 Implement Event Publisher
```csharp
// PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs
public class MassTransitEventPublisher : IEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<MassTransitEventPublisher> _logger;

    public MassTransitEventPublisher(
        IPublishEndpoint publishEndpoint,
        ILogger<MassTransitEventPublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) 
        where T : IntegrationEvent
    {
        _logger.LogInformation("Publishing event {EventType} with ID {EventId}", 
            @event.EventType, @event.Id);
        
        await _publishEndpoint.Publish(@event, cancellationToken);
    }
}
```

#### 3.6 Configure MassTransit
```csharp
// In Program.cs
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();
    
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"]);
            h.Password(builder.Configuration["RabbitMQ:Password"]);
        });
        
        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
```

---

### 4. Update ProductsController to Publish Events

```csharp
// In ProductsController.cs

private readonly IEventPublisher _eventPublisher;

public ProductsController(
    ApplicationDbContext context,
    IEventPublisher eventPublisher)
{
    _context = context;
    _eventPublisher = eventPublisher;
}

[HttpPost]
public async Task<ActionResult<Product>> CreateProduct(CreateProductDto dto)
{
    // ... existing creation logic ...
    
    await _context.SaveChangesAsync();
    
    // Publish event
    await _eventPublisher.PublishAsync(new ProductCreatedEvent
    {
        ProductId = product.Id,
        Name = product.Name,
        SKU = product.SKU,
        Price = product.Price,
        CategoryId = product.CategoryId,
        Barcode = product.Barcode
    });
    
    return CreatedAtAction(...);
}

[HttpPut("{id}")]
public async Task<IActionResult> UpdateProduct(Guid id, UpdateProductDto dto)
{
    var oldPrice = product.Price;
    
    // ... existing update logic ...
    
    await _context.SaveChangesAsync();
    
    // Publish product updated event
    await _eventPublisher.PublishAsync(new ProductUpdatedEvent
    {
        ProductId = product.Id,
        Name = product.Name,
        SKU = product.SKU,
        Price = product.Price,
        CategoryId = product.CategoryId
    });
    
    // Publish price changed event if price changed
    if (oldPrice != product.Price)
    {
        await _eventPublisher.PublishAsync(new ProductPriceChangedEvent
        {
            ProductId = product.Id,
            OldPrice = oldPrice,
            NewPrice = product.Price
        });
    }
    
    return NoContent();
}
```

---

### 5. Frontend Updates

#### 5.1 Add SKU Field to ProductForm
- Add SKU input field (required, unique)
- Add auto-generate SKU button
- Add SKU validation (alphanumeric, unique)

#### 5.2 Add Publish Status Field
- Add dropdown for draft/published/archived
- Add "Online Visible" toggle
- Add "Searchable" toggle

#### 5.3 Add Shipping Fields
- Add weight input with unit selector (kg/lb)
- Add dimensions inputs (length, width, height) with unit selector
- Add low stock threshold input

#### 5.4 Add SEO Section (Collapsible)
- Add URL slug input with auto-generate
- Add meta title input
- Add meta description textarea
- Add SEO preview

---

## 📝 Migration Scripts

### Migration 1: Add Product E-commerce Fields
```sql
-- Add e-commerce fields to Products table
ALTER TABLE "Products"
ADD COLUMN "SKU" VARCHAR(50),
ADD COLUMN "Slug" VARCHAR(255),
ADD COLUMN "MetaTitle" VARCHAR(255),
ADD COLUMN "MetaDescription" TEXT,
ADD COLUMN "Weight" DECIMAL(10,3) DEFAULT 0,
ADD COLUMN "Length" DECIMAL(10,2) DEFAULT 0,
ADD COLUMN "Width" DECIMAL(10,2) DEFAULT 0,
ADD COLUMN "Height" DECIMAL(10,2) DEFAULT 0,
ADD COLUMN "WeightUnit" VARCHAR(10) DEFAULT 'kg',
ADD COLUMN "DimensionUnit" VARCHAR(10) DEFAULT 'cm',
ADD COLUMN "PublishStatus" VARCHAR(20) DEFAULT 'draft',
ADD COLUMN "IsOnlineVisible" BOOLEAN DEFAULT FALSE,
ADD COLUMN "IsSearchable" BOOLEAN DEFAULT TRUE,
ADD COLUMN "LowStockThreshold" INTEGER DEFAULT 10;

-- Create unique index on SKU
CREATE UNIQUE INDEX "IX_Products_SKU" ON "Products" ("SKU") WHERE "SKU" IS NOT NULL AND "IsDeleted" = FALSE;

-- Create unique index on Slug
CREATE UNIQUE INDEX "IX_Products_Slug" ON "Products" ("Slug") WHERE "Slug" IS NOT NULL AND "IsDeleted" = FALSE;
```

### Migration 2: Create Orders Table
```sql
-- Create Orders table
CREATE TABLE "Orders" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "OrderNumber" VARCHAR(50) NOT NULL UNIQUE,
    "CustomerId" UUID REFERENCES "Customers"("Id"),
    
    -- Guest checkout
    "GuestEmail" VARCHAR(255),
    "GuestPhone" VARCHAR(50),
    "GuestName" VARCHAR(100),
    
    -- Status
    "Status" INTEGER DEFAULT 0,
    "PaymentStatus" INTEGER DEFAULT 0,
    
    -- Addresses
    "ShippingAddress" TEXT NOT NULL,
    "BillingAddress" TEXT,
    "ShippingCity" VARCHAR(100),
    "ShippingState" VARCHAR(100),
    "ShippingZipCode" VARCHAR(20),
    "ShippingCountry" VARCHAR(100),
    
    -- Shipping
    "ShippingMethodId" UUID,
    "ShippingCost" DECIMAL(18,2) DEFAULT 0,
    "TrackingNumber" VARCHAR(100),
    "Carrier" VARCHAR(100),
    
    -- Financials
    "Subtotal" DECIMAL(18,2) NOT NULL,
    "DiscountAmount" DECIMAL(18,2) DEFAULT 0,
    "TaxAmount" DECIMAL(18,2) DEFAULT 0,
    "Total" DECIMAL(18,2) NOT NULL,
    "Currency" VARCHAR(3) DEFAULT 'USD',
    
    -- Coupon
    "CouponCode" VARCHAR(50),
    "CouponId" UUID,
    
    -- Payment
    "PaymentMethod" INTEGER,
    "PaymentReference" VARCHAR(100),
    "PaidAt" TIMESTAMP,
    
    -- Notes
    "CustomerNotes" TEXT,
    "AdminNotes" TEXT,
    
    -- Source
    "Source" VARCHAR(50) DEFAULT 'Ecommerce',
    "SourceOrderId" VARCHAR(100),
    
    -- Timestamps
    "ShippedAt" TIMESTAMP,
    "DeliveredAt" TIMESTAMP,
    "CancelledAt" TIMESTAMP,
    "CancellationReason" TEXT,
    
    -- Audit fields
    "CreatedAt" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    "CreatedByUserId" UUID,
    "UpdatedByUserId" UUID,
    "IsDeleted" BOOLEAN DEFAULT FALSE
);

-- Create OrderItems table
CREATE TABLE "OrderItems" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "OrderId" UUID NOT NULL REFERENCES "Orders"("Id") ON DELETE CASCADE,
    "ProductId" UUID NOT NULL REFERENCES "Products"("Id"),
    "UnitId" UUID REFERENCES "Units"("Id"),
    "WarehouseId" UUID REFERENCES "Warehouses"("Id"),
    
    "SKU" VARCHAR(50) NOT NULL,
    "ProductName" VARCHAR(255) NOT NULL,
    "VariantName" VARCHAR(255),
    
    "Quantity" INTEGER NOT NULL,
    "UnitPrice" DECIMAL(18,2) NOT NULL,
    "DiscountAmount" DECIMAL(18,2) DEFAULT 0,
    "TaxAmount" DECIMAL(18,2) DEFAULT 0,
    "Total" DECIMAL(18,2) NOT NULL,
    
    "Notes" TEXT,
    
    "CreatedAt" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    "IsDeleted" BOOLEAN DEFAULT FALSE
);

-- Indexes
CREATE INDEX "IX_Orders_CustomerId" ON "Orders" ("CustomerId");
CREATE INDEX "IX_Orders_Status" ON "Orders" ("Status");
CREATE INDEX "IX_Orders_CreatedAt" ON "Orders" ("CreatedAt");
CREATE INDEX "IX_Orders_Source" ON "Orders" ("Source");
CREATE INDEX "IX_OrderItems_OrderId" ON "OrderItems" ("OrderId");
CREATE INDEX "IX_OrderItems_ProductId" ON "OrderItems" ("ProductId");
```

### Migration 3: Create Shipping Methods Table
```sql
CREATE TABLE "ShippingMethods" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Name" VARCHAR(100) NOT NULL,
    "NameAr" VARCHAR(100),
    "Description" TEXT,
    
    "BaseRate" DECIMAL(18,2) DEFAULT 0,
    "PerKgRate" DECIMAL(18,2) DEFAULT 0,
    "FreeShippingThreshold" DECIMAL(18,2),
    
    "EstimatedDeliveryDaysMin" INTEGER DEFAULT 1,
    "EstimatedDeliveryDaysMax" INTEGER DEFAULT 5,
    
    "IsActive" BOOLEAN DEFAULT TRUE,
    "SortOrder" INTEGER DEFAULT 0,
    
    "CreatedAt" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    "IsDeleted" BOOLEAN DEFAULT FALSE
);

-- Seed default shipping methods
INSERT INTO "ShippingMethods" ("Id", "Name", "NameAr", "BaseRate", "EstimatedDeliveryDaysMin", "EstimatedDeliveryDaysMax", "SortOrder")
VALUES 
    (gen_random_uuid(), 'Standard Shipping', 'شحن عادي', 10.00, 5, 7, 1),
    (gen_random_uuid(), 'Express Shipping', 'شحن سريع', 25.00, 2, 3, 2),
    (gen_random_uuid(), 'Free Shipping', 'شحن مجاني', 0.00, 7, 14, 3);
```

### Migration 4: Create Coupons Table
```sql
CREATE TABLE "Coupons" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Code" VARCHAR(50) NOT NULL UNIQUE,
    "Description" TEXT,
    
    "DiscountType" INTEGER DEFAULT 0, -- 0: Percentage, 1: Fixed
    "DiscountValue" DECIMAL(18,2) NOT NULL,
    
    "MinOrderAmount" DECIMAL(18,2),
    "MaxDiscountAmount" DECIMAL(18,2),
    
    "MaxUsage" INTEGER,
    "CurrentUsage" INTEGER DEFAULT 0,
    "MaxUsagePerCustomer" INTEGER,
    
    "StartDate" TIMESTAMP,
    "EndDate" TIMESTAMP,
    
    "IsActive" BOOLEAN DEFAULT TRUE,
    "FirstOrderOnly" BOOLEAN DEFAULT FALSE,
    
    "ApplicableProductIds" TEXT, -- JSON array
    "ApplicableCategoryIds" TEXT, -- JSON array
    
    "CreatedAt" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    "IsDeleted" BOOLEAN DEFAULT FALSE
);

CREATE INDEX "IX_Coupons_Code" ON "Coupons" ("Code");
CREATE INDEX "IX_Coupons_IsActive" ON "Coupons" ("IsActive");
```

---

## 📅 Implementation Timeline

| Week | Tasks | Status |
|------|-------|--------|
| **Week 1 - Day 1-2** | Database schema updates (Product fields) | ⬜ |
| **Week 1 - Day 3-4** | Create new entities (Order, OrderItem, ShippingMethod, Coupon) | ⬜ |
| **Week 1 - Day 5** | Create database migrations and apply | ⬜ |
| **Week 2 - Day 1-2** | Update ProductsController with event publishing | ⬜ |
| **Week 2 - Day 3** | Enable Swagger + API versioning | ⬜ |
| **Week 2 - Day 4** | Add rate limiting | ⬜ |
| **Week 2 - Day 5** | Frontend updates (SKU, weight, SEO fields) | ⬜ |

---

## 🧪 Testing Checklist

- [ ] Verify product SKU uniqueness
- [ ] Verify product slug generation
- [ ] Verify weight/dimension fields save correctly
- [ ] Test publish status transitions
- [ ] Test new Order creation
- [ ] Test ShippingMethod CRUD
- [ ] Test Coupon CRUD
- [ ] Verify event publishing (with RabbitMQ)
- [ ] Test API rate limiting
- [ ] Verify Swagger documentation

---

## 📄 Files to Create/Modify

### New Files
1. `PointOfSale.Core/Entities/Order.cs`
2. `PointOfSale.Core/Entities/OrderItem.cs`
3. `PointOfSale.Core/Entities/ShippingMethod.cs`
4. `PointOfSale.Core/Entities/Coupon.cs`
5. `PointOfSale.Core/Enums/OrderStatus.cs`
6. `PointOfSale.Core/Enums/PaymentStatus.cs`
7. `PointOfSale.Core/Enums/DiscountType.cs`
8. `PointOfSale.Core/Events/IntegrationEvent.cs`
9. `PointOfSale.Core/Events/ProductEvents.cs`
10. `PointOfSale.Core/Interfaces/IEventPublisher.cs`
11. `PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs`
12. `PointOfSale.API/Controllers/OrdersController.cs`
13. `PointOfSale.API/Controllers/ShippingMethodsController.cs`
14. `PointOfSale.API/Controllers/CouponsController.cs`

### Modified Files
1. `PointOfSale.Core/Entities/Product.cs` - Add e-commerce fields
2. `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs` - Add new DbSets
3. `PointOfSale.API/Program.cs` - Add Swagger, versioning, rate limiting, MassTransit
4. `PointOfSale.API/Controllers/ProductsController.cs` - Add event publishing
5. `PointOfSale.Web/src/components/ProductForm.jsx` - Add new fields

---

## ✅ Ready to Start?

Once you approve this plan, I'll begin implementing Phase 0 starting with:

1. **Database Schema Updates** - Add new fields to Product entity
2. **Create New Entities** - Order, OrderItem, ShippingMethod, Coupon
3. **Create Migrations** - Apply all database changes
4. **API Enhancements** - Swagger, versioning, rate limiting
5. **Event Infrastructure** - MassTransit setup
6. **Frontend Updates** - Product form enhancements

**Shall I proceed with the implementation?**
