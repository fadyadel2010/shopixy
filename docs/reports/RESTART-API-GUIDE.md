# Quick Guide: Restarting the API with Authentication

## The API is currently running and needs to be restarted to load the new authentication controller.

### Option 1: Use the Restart Script (Easiest)

Double-click or run:
```
restart-api.bat
```

This will:
1. Stop the running API
2. Rebuild with the new authentication controller
3. Start it again

### Option 2: Manual Restart

1. **Stop the API**: Press `Ctrl+C` in the terminal where the API is running
2. **Rebuild**: 
   ```bash
   cd PointOfSale.API
   dotnet build
   dotnet run
   ```

## After the API Restarts

### Create an Admin User

You can now register via the API or frontend!

**Using PowerShell:**
```powershell
$body = @{
    username = "admin"
    email = "admin@pos.com"
    password = "Admin@123"
    firstName = "Admin"
    lastName = "User"
    phone = ""
    role = 0
} | ConvertTo-Json

Invoke-WebRequest -Uri "https://localhost:7000/api/auth/register" `
    -Method POST `
    -Body $body `
    -ContentType "application/json" `
    -SkipCertificateCheck
```

**Or just use the Login Page:**
1. Go to http://localhost:5173
2. You won't be able to login yet (no users exist)
3. But you can use the register endpoint above to create the first admin user
4. Then login with: `admin` / `Admin@123`

### Available Auth Endpoints

- `POST /api/auth/register` - Create new user
- `POST /api/auth/login` - Login and get JWT token
- `POST /api/auth/refresh` - Refresh access token

### User Roles

- `0` = Admin (full access)
- `1` = Manager
- `2` = Cashier  
- `3` = Warehouse

## Testing the Login

Once you've created a user, you can login through the beautiful UI at http://localhost:5173!

The frontend will:
- Store the JWT token
- Automatically refresh it when it expires
- Show you the Dashboard, Products, POS, and other pages
