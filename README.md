# Falaza Lodge Jozini Backend

ASP.NET Core API for Falaza Lodge accommodation bookings, event enquiries, availability control and lodge administration.

## Implemented

- Public booking and venue enquiry submission
- Live room availability checks
- Overlap protection before accommodation bookings can be confirmed
- Admin login with JWT authentication
- Booking dashboard and dedicated booking detail API
- Booking status workflow: New, Confirmed, Declined, Cancelled
- Guest email notifications on confirmation, decline or cancellation
- Lodge email notification when a new request arrives
- Admin user creation, enable/disable and password changes
- Forgotten-password email reset flow
- Audit trail for booking/admin actions
- EF Core migrations
- SQLite persistence for the initial deployment
- Automatic daily SQLite backups with retention cleanup
- Manual admin-triggered backup endpoint
- Database and liveness health endpoints
- Persistent application error logging
- Swagger API documentation
- Docker support
- GitHub Actions build verification

## Requirements

- .NET 9 SDK

## First run

Set the first admin account and JWT secret before starting the API.

### PowerShell

```powershell
$env:FALAZA_ADMIN_EMAIL="admin@falazalodge.co.za"
$env:FALAZA_ADMIN_PASSWORD="replace-with-a-strong-password"
$env:FALAZA_JWT_KEY="replace-with-a-long-random-secret-at-least-32-characters"
dotnet restore
dotnet run
```

The application applies pending EF Core migrations automatically and seeds the initial room inventory. The first admin is only created when no admin accounts exist.

## Email configuration

Use deployment secrets or environment variables instead of committing credentials.

Example environment variables:

```text
Email__SmtpHost=smtp.example.com
Email__SmtpPort=587
Email__EnableSsl=true
Email__Username=...
Email__Password=...
Email__FromAddress=bookings@falazalodge.co.za
Email__AdminRecipients__0=reservations@falazalodge.co.za
FrontendPublicUrl=https://www.falazalodge.co.za
```

If SMTP is not configured, booking operations still work but the service logs that the email was skipped.

## Availability rules

Accommodation bookings are linked to a room record. A booking cannot be changed to `Confirmed` when another confirmed booking for the same room overlaps its stay.

The overlap rule uses checkout as the departure boundary, so a guest may check out on the same date another guest checks in.

Public endpoint:

```
GET /api/availability?checkIn=2026-10-10&checkOut=2026-10-12
```

## Admin account management

Authenticated admins can:

- list staff accounts
- create additional admins
- enable or disable another admin
- change their own password

Forgotten-password flow:

- `POST /api/auth/forgot-password`
- `POST /api/auth/reset-password`

Reset tokens expire after 30 minutes and are stored as hashes.

## Backups

For the current SQLite deployment:

- a background service creates a backup approximately every 24 hours
- backups default to the `Backups` directory
- files older than 14 days are removed automatically
- admins can trigger a backup with `POST /api/admin/backups`

Configure:

```text
Backups__Directory=/persistent/backups
Backups__RetentionDays=14
```

The database and backup directory must be placed on persistent storage in production. A container-local filesystem alone is not sufficient.

## Health and error monitoring

- `GET /health/live` - process liveness
- `GET /health` - includes database health
- `GET /api/admin/errors` - latest persisted unhandled application errors

Unhandled exceptions receive a trace ID, are written to application logs and are also stored in the database for admin review.

## Main endpoints

### Public

- `GET /api/availability`
- `POST /api/bookings`
- `POST /api/auth/login`
- `POST /api/auth/forgot-password`
- `POST /api/auth/reset-password`
- `GET /health/live`
- `GET /health`

### Admin

Requires `Authorization: Bearer <token>`.

- `GET /api/admin/dashboard`
- `GET /api/admin/rooms`
- `GET /api/admin/bookings`
- `GET /api/admin/bookings/{id}`
- `PATCH /api/admin/bookings/{id}/status`
- `GET /api/admin/users`
- `POST /api/admin/users`
- `PATCH /api/admin/users/{id}/active`
- `POST /api/admin/change-password`
- `GET /api/admin/audit`
- `GET /api/admin/errors`
- `POST /api/admin/backups`

## Production deployment

The default frontend origin is `http://localhost:5173`. Configure `FrontendOrigins` for the deployed website.

SQLite is suitable for a single-instance first deployment. If the system later moves to multiple application instances or significantly higher booking volume, migrate the EF Core provider to PostgreSQL or SQL Server and replace the SQLite file-backup service with managed database backups.
