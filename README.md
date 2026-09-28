# Falaza Lodge Jozini Backend

ASP.NET Core API for Falaza Lodge's accommodation bookings, event enquiries and lodge admin dashboard.

## What is implemented

- Public booking / venue enquiry submission
- Admin login with JWT authentication
- Admin dashboard totals
- Booking list and search
- Booking status updates: New, Confirmed, Declined, Cancelled
- SQLite persistence for the initial deployment
- CORS configuration for the Falaza frontend
- Swagger API documentation

## Requirements

- .NET 9 SDK

## Local setup

Set the first admin credentials before the first run.

### PowerShell

```powershell
$env:FALAZA_ADMIN_EMAIL="admin@falazalodge.co.za"
$env:FALAZA_ADMIN_PASSWORD="replace-with-a-strong-password"
$env:FALAZA_JWT_KEY="replace-with-a-long-random-secret-at-least-32-characters"
dotnet restore
dotnet run
```

On first startup, the API creates `falaza.db` and seeds the first admin account from those environment variables.

The default frontend origin is:

```
http://localhost:5173
```

For production, configure `FrontendOrigins`, the database connection string and JWT secret using environment variables or deployment configuration.

## Main endpoints

### Public

- `POST /api/bookings`
- `POST /api/auth/login`

### Admin

Requires `Authorization: Bearer <token>`.

- `GET /api/admin/dashboard`
- `GET /api/admin/bookings`
- `GET /api/admin/bookings/{id}`
- `PATCH /api/admin/bookings/{id}/status`

## Production note

SQLite is appropriate for the first working version and a low-volume lodge deployment. If usage grows or the application is hosted across multiple instances, migrate the EF Core provider to PostgreSQL or SQL Server.
