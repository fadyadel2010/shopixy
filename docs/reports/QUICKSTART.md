# Quick Start Guide

## 🚀 Getting Started

### 1. Database Setup

Run the database setup script:

**Option A - PowerShell (Recommended):**
```powershell
.\setup-database.ps1
```

**Option B - Command Prompt:**
```cmd
setup-database.bat
```

**Option C - Manual:**
```bash
# Install EF Core tools
dotnet tool install --global dotnet-ef --version 9.0.1

# Create migration
dotnet ef migrations add InitialCreate --project PointOfSale.Infrastructure --startup-project PointOfSale.API

# Apply to database
dotnet ef database update --project PointOfSale.Infrastructure --startup-project PointOfSale.API
```

### 2. Run the Backend API

```bash
cd PointOfSale.API
dotnet run
```

The API will start at:
- **HTTPS**: https://localhost:7000
- **HTTP**: http://localhost:5000
- **Swagger**: https://localhost:7000/swagger
- **Hangfire Dashboard**: https://localhost:7000/hangfire

### 3. Create Initial Admin User

You'll need to create an admin user to access the system. You can do this via:

1. **Swagger UI** - Navigate to https://localhost:7000/swagger
2. Use the `/api/auth/register` endpoint (to be created)
3. Or insert directly into the database

### 4. Frontend Setup (Coming Soon)

The React frontend will be set up in the `PointOfSale.Web` directory.

```bash
cd PointOfSale.Web
npm install
npm run dev
```

## 📝 Database Configuration

Inject the database connection through environment variables or `dotnet user-secrets`:

```bash
export ConnectionStrings__DefaultConnection="Host=<db-host>;Port=5432;Database=<db-name>;Username=<db-user>;Password=<retrieve-from-secret-manager>"
```

## ✅ What's Implemented

### Backend (Complete)
- ✅ Clean Architecture structure
- ✅ Entity Framework Core with PostgreSQL
- ✅ All domain models (20+ entities)
- ✅ JWT Authentication with refresh tokens
- ✅ Hangfire for background jobs
- ✅ SignalR for real-time updates
- ✅ Sample controllers (Products, Warehouses)
- ✅ Multi-warehouse support
- ✅ Comprehensive tax handling

### Features Ready
- Customer & Supplier management
- Product catalog with variants
- Multi-warehouse inventory
- Stock movement tracking
- Purchase invoices
- Sales invoices (POS)
- Returns processing
- Transaction history

### Next Steps
1. ✅ Run database migration
2. ⏳ Create authentication controller
3. ⏳ Create remaining API controllers
4. ⏳ Set up React frontend
5. ⏳ Build POS interface
6. ⏳ Implement reporting

## 🔧 Troubleshooting

### EF Tools Not Found
If you get "dotnet-ef not found", run:
```bash
dotnet tool install --global dotnet-ef --version 9.0.1
```

Then restart your terminal/PowerShell.

### Database Connection Issues
1. Verify PostgreSQL is running
2. Check `ConnectionStrings__DefaultConnection` in your environment or user-secrets
3. Ensure database `PosEcommerceDb` exists
4. Verify network access to your PostgreSQL host and port

### Build Errors
```bash
# Clean and rebuild
dotnet clean
dotnet build
```

## 📚 API Documentation

Once the API is running, visit **https://localhost:7000/swagger** for interactive API documentation.

## 🎯 Key Features

### Multi-Warehouse
- Stock tracked per warehouse
- Warehouse-specific transactions
- Inter-warehouse transfers
- Reserved quantity support

### Tax Configuration
- Product-level tax rates
- Tax-inclusive/exclusive pricing
- Default global tax rate
- Regional tax variations (ready)

### Authentication
- JWT with refresh tokens
- Role-based authorization
- 60-minute session timeout
- Extensible for OIDC integration

### Real-time Updates
- SignalR for live stock updates
- Multi-user support
- Instant inventory sync

## 📞 Support

For issues or questions, refer to the main README.md file.



