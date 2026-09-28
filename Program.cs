using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FalazaLodge.Api;
using FalazaLodge.Api.Data;
using FalazaLodge.Api.Middleware;
using FalazaLodge.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=falaza.db"));

var jwtKey = builder.Configuration["Jwt:Key"] ?? Environment.GetEnvironmentVariable("FALAZA_JWT_KEY");
if (string.IsNullOrWhiteSpace(jwtKey))
{
    if (builder.Environment.IsDevelopment())
        jwtKey = "dev-only-change-this-falaza-jwt-key-2026-at-least-32-chars";
    else
        throw new InvalidOperationException("JWT key is required. Set Jwt__Key or FALAZA_JWT_KEY.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "FalazaLodge";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "FalazaLodgeAdmin";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<IPasswordHasher<AdminUser>, PasswordHasher<AdminUser>>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BackupService>());
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var configuredOrigins = builder.Configuration.GetSection("FrontendOrigins").Get<string[]>() ?? ["http://localhost:5173"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(configuredOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    if (!await db.Rooms.AnyAsync())
    {
        db.Rooms.AddRange(
            new Room { Name = "Deluxe Double Room", Capacity = 3 },
            new Room { Name = "Deluxe Double or Twin Room", Capacity = 3 },
            new Room { Name = "Three-Bedroom Apartment", Capacity = 4 },
            new Room { Name = "Deluxe Double Room with Balcony", Capacity = 2 });
        await db.SaveChangesAsync();
    }

    if (!await db.AdminUsers.AnyAsync())
    {
        var adminEmail = builder.Configuration["Admin:Email"] ?? Environment.GetEnvironmentVariable("FALAZA_ADMIN_EMAIL");
        var adminPassword = builder.Configuration["Admin:Password"] ?? Environment.GetEnvironmentVariable("FALAZA_ADMIN_PASSWORD");

        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
        {
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminUser>>();
            var admin = new AdminUser
            {
                Email = adminEmail.Trim().ToLowerInvariant(),
                DisplayName = builder.Configuration["Admin:DisplayName"] ?? "Falaza Lodge Admin"
            };
            admin.PasswordHash = hasher.HashPassword(admin, adminPassword);
            db.AdminUsers.Add(admin);
            db.AuditLogs.Add(new AuditLog
            {
                AdminUserId = admin.Id,
                AdminEmail = admin.Email,
                Action = "AdminSeeded",
                EntityType = "AdminUser",
                EntityId = admin.Id.ToString(),
                Details = "Initial admin account created from deployment configuration."
            });
            await db.SaveChangesAsync();
        }
        else
        {
            app.Logger.LogWarning("No admin user exists. Set FALAZA_ADMIN_EMAIL and FALAZA_ADMIN_PASSWORD, then restart once to seed the first admin.");
        }
    }
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("Frontend");
app.UseMiddleware<ErrorLoggingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapGet("/health/live", () => Results.Ok(new
{
    status = "healthy",
    service = "Falaza Lodge Jozini API",
    timestampUtc = DateTimeOffset.UtcNow
}));

app.MapGet("/", () => Results.Ok(new
{
    service = "Falaza Lodge Jozini API",
    status = "online"
}));

app.MapGet("/api/availability", async (DateOnly checkIn, DateOnly checkOut, AppDbContext db) =>
{
    if (checkOut <= checkIn)
        return Results.BadRequest(new { message = "Check-out must be after check-in." });

    var rooms = await db.Rooms.AsNoTracking()
        .Where(x => x.IsActive)
        .OrderBy(x => x.Name)
        .ToListAsync();

    var confirmed = await db.BookingRequests.AsNoTracking()
        .Where(x => x.Status == BookingStatuses.Confirmed &&
                    x.RoomId != null &&
                    x.CheckIn != null &&
                    x.CheckOut != null &&
                    x.CheckIn < checkOut &&
                    x.CheckOut > checkIn)
        .Select(x => x.RoomId!.Value)
        .Distinct()
        .ToListAsync();

    var blocked = confirmed.ToHashSet();

    return Results.Ok(rooms.Select(room => new
    {
        room.Id,
        room.Name,
        room.Capacity,
        available = !blocked.Contains(room.Id)
    }));
});

app.MapPost("/api/bookings", async (
    CreateBookingRequest request,
    AppDbContext db,
    IEmailService emailService) =>
{
    var errors = ValidateBooking(request);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    Room? selectedRoom = null;
    if (request.EnquiryType.Equals(EnquiryTypes.Accommodation, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(request.RoomPreference) &&
        !request.RoomPreference.Contains("No preference", StringComparison.OrdinalIgnoreCase))
    {
        selectedRoom = await db.Rooms.SingleOrDefaultAsync(x =>
            x.IsActive && x.Name == request.RoomPreference.Trim());

        if (selectedRoom is null)
            return Results.BadRequest(new { message = "The selected room is not available in the room inventory." });
    }

    var booking = new BookingRequest
    {
        EnquiryType = request.EnquiryType.Trim(),
        CheckIn = request.CheckIn,
        CheckOut = request.CheckOut,
        Adults = request.Adults,
        Children = request.Children,
        RoomId = selectedRoom?.Id,
        RoomPreference = Clean(request.RoomPreference),
        GuestName = request.GuestName.Trim(),
        Phone = request.Phone.Trim(),
        Email = request.Email.Trim().ToLowerInvariant(),
        Details = Clean(request.Details),
        Status = BookingStatuses.New
    };

    db.BookingRequests.Add(booking);
    await db.SaveChangesAsync();

    try
    {
        await emailService.SendNewBookingNotificationAsync(booking);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to send new booking notification for {BookingId}", booking.Id);
    }

    return Results.Created($"/api/bookings/{booking.Id}", new
    {
        booking.Id,
        booking.Status,
        booking.CreatedAtUtc,
        message = "Your request has been received. Falaza Lodge will confirm availability directly."
    });
});

app.MapPost("/api/auth/login", async (
    LoginRequest request,
    AppDbContext db,
    IPasswordHasher<AdminUser> hasher) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    var admin = await db.AdminUsers.SingleOrDefaultAsync(x => x.Email == email && x.IsActive);
    if (admin is null)
        return Results.Unauthorized();

    var verification = hasher.VerifyHashedPassword(admin, admin.PasswordHash, request.Password);
    if (verification == PasswordVerificationResult.Failed)
        return Results.Unauthorized();

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, admin.Id.ToString()),
        new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.Email, admin.Email),
        new Claim(ClaimTypes.Email, admin.Email),
        new Claim(ClaimTypes.Name, admin.DisplayName),
        new Claim(ClaimTypes.Role, "Admin")
    };

    var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    var expires = DateTime.UtcNow.AddHours(8);
    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: expires,
        signingCredentials: credentials);

    db.AuditLogs.Add(new AuditLog
    {
        AdminUserId = admin.Id,
        AdminEmail = admin.Email,
        Action = "Login",
        EntityType = "AdminUser",
        EntityId = admin.Id.ToString()
    });
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        token = new JwtSecurityTokenHandler().WriteToken(token),
        expiresAtUtc = expires,
        admin = new { admin.Id, admin.Email, admin.DisplayName }
    });
});

app.MapPost("/api/auth/forgot-password", async (
    RequestPasswordResetRequest request,
    AppDbContext db,
    IEmailService emailService) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    var admin = await db.AdminUsers.SingleOrDefaultAsync(x => x.Email == email && x.IsActive);

    if (admin is not null)
    {
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenHash = HashToken(rawToken);
        var reset = new PasswordResetToken
        {
            AdminUserId = admin.Id,
            TokenHash = tokenHash,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        db.PasswordResetTokens.Add(reset);
        await db.SaveChangesAsync();

        var frontendUrl = builder.Configuration["FrontendPublicUrl"] ?? "http://localhost:5173";
        var resetUrl = $"{frontendUrl.TrimEnd('/')}/admin/reset-password?token={Uri.EscapeDataString(rawToken)}";

        try
        {
            await emailService.SendPasswordResetAsync(admin, resetUrl);
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Failed to send password reset email for admin {AdminId}", admin.Id);
        }
    }

    return Results.Ok(new { message = "If that admin account exists, a password reset email has been sent." });
});

app.MapPost("/api/auth/reset-password", async (
    ResetPasswordRequest request,
    AppDbContext db,
    IPasswordHasher<AdminUser> hasher) =>
{
    if (request.NewPassword.Length < 10)
        return Results.BadRequest(new { message = "Password must be at least 10 characters." });

    var tokenHash = HashToken(request.Token);
    var reset = await db.PasswordResetTokens
        .Include(x => x.AdminUser)
        .SingleOrDefaultAsync(x =>
            x.TokenHash == tokenHash &&
            x.UsedAtUtc == null &&
            x.ExpiresAtUtc > DateTimeOffset.UtcNow);

    if (reset is null || !reset.AdminUser.IsActive)
        return Results.BadRequest(new { message = "This password reset link is invalid or has expired." });

    reset.AdminUser.PasswordHash = hasher.HashPassword(reset.AdminUser, request.NewPassword);
    reset.UsedAtUtc = DateTimeOffset.UtcNow;

    db.AuditLogs.Add(new AuditLog
    {
        AdminUserId = reset.AdminUser.Id,
        AdminEmail = reset.AdminUser.Email,
        Action = "PasswordReset",
        EntityType = "AdminUser",
        EntityId = reset.AdminUser.Id.ToString()
    });

    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Password updated. You can now sign in." });
});

var adminApi = app.MapGroup("/api/admin")
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

adminApi.MapGet("/dashboard", async (AppDbContext db) =>
{
    var today = DateOnly.FromDateTime(DateTime.UtcNow);

    var total = await db.BookingRequests.CountAsync();
    var newCount = await db.BookingRequests.CountAsync(x => x.Status == BookingStatuses.New);
    var confirmed = await db.BookingRequests.CountAsync(x => x.Status == BookingStatuses.Confirmed);
    var arrivalsToday = await db.BookingRequests.CountAsync(x =>
        x.Status == BookingStatuses.Confirmed && x.CheckIn == today);
    var departuresToday = await db.BookingRequests.CountAsync(x =>
        x.Status == BookingStatuses.Confirmed && x.CheckOut == today);
    var eventEnquiries = await db.BookingRequests.CountAsync(x =>
        x.Status == BookingStatuses.New && x.EnquiryType != EnquiryTypes.Accommodation);

    var recent = await db.BookingRequests.AsNoTracking()
        .OrderByDescending(x => x.CreatedAtUtc)
        .Take(8)
        .Select(x => new
        {
            x.Id,
            x.GuestName,
            x.EnquiryType,
            x.CheckIn,
            x.CheckOut,
            x.RoomPreference,
            x.Status,
            x.CreatedAtUtc
        })
        .ToListAsync();

    return Results.Ok(new
    {
        total,
        newRequests = newCount,
        confirmed,
        arrivalsToday,
        departuresToday,
        eventEnquiries,
        recent
    });
});

adminApi.MapGet("/rooms", async (AppDbContext db) =>
    Results.Ok(await db.Rooms.AsNoTracking().OrderBy(x => x.Name).ToListAsync()));

adminApi.MapGet("/bookings", async (
    string? status,
    string? type,
    string? search,
    AppDbContext db) =>
{
    var query = db.BookingRequests.AsNoTracking().Include(x => x.Room).AsQueryable();

    if (!string.IsNullOrWhiteSpace(status))
        query = query.Where(x => x.Status == status);

    if (!string.IsNullOrWhiteSpace(type))
        query = query.Where(x => x.EnquiryType == type);

    if (!string.IsNullOrWhiteSpace(search))
    {
        var q = search.Trim().ToLower();
        query = query.Where(x =>
            x.GuestName.ToLower().Contains(q) ||
            x.Email.ToLower().Contains(q) ||
            x.Phone.ToLower().Contains(q));
    }

    var results = await query
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new
        {
            x.Id,
            x.EnquiryType,
            x.CheckIn,
            x.CheckOut,
            x.Adults,
            x.Children,
            x.RoomId,
            RoomName = x.Room != null ? x.Room.Name : null,
            x.RoomPreference,
            x.GuestName,
            x.Phone,
            x.Email,
            x.Details,
            x.Status,
            x.CreatedAtUtc,
            x.UpdatedAtUtc
        })
        .ToListAsync();

    return Results.Ok(results);
});

adminApi.MapGet("/bookings/{id:guid}", async (Guid id, AppDbContext db) =>
{
    var booking = await db.BookingRequests.AsNoTracking()
        .Include(x => x.Room)
        .SingleOrDefaultAsync(x => x.Id == id);

    if (booking is null)
        return Results.NotFound();

    var audit = await db.AuditLogs.AsNoTracking()
        .Where(x => x.EntityType == "BookingRequest" && x.EntityId == id.ToString())
        .OrderByDescending(x => x.CreatedAtUtc)
        .ToListAsync();

    return Results.Ok(new
    {
        booking.Id,
        booking.EnquiryType,
        booking.CheckIn,
        booking.CheckOut,
        booking.Adults,
        booking.Children,
        booking.RoomId,
        RoomName = booking.Room?.Name,
        booking.RoomPreference,
        booking.GuestName,
        booking.Phone,
        booking.Email,
        booking.Details,
        booking.Status,
        booking.CreatedAtUtc,
        booking.UpdatedAtUtc,
        auditTrail = audit
    });
});

adminApi.MapPatch("/bookings/{id:guid}/status", async (
    Guid id,
    UpdateBookingStatusRequest request,
    HttpContext httpContext,
    AppDbContext db,
    IEmailService emailService) =>
{
    if (!BookingStatuses.All.Contains(request.Status))
        return Results.BadRequest(new { message = "Invalid booking status." });

    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

    var booking = await db.BookingRequests
        .Include(x => x.Room)
        .SingleOrDefaultAsync(x => x.Id == id);

    if (booking is null)
        return Results.NotFound();

    var normalizedStatus = BookingStatuses.All.First(x =>
        x.Equals(request.Status, StringComparison.OrdinalIgnoreCase));

    if (normalizedStatus == BookingStatuses.Confirmed &&
        booking.EnquiryType == EnquiryTypes.Accommodation)
    {
        if (booking.RoomId is null || booking.CheckIn is null || booking.CheckOut is null)
            return Results.BadRequest(new { message = "A room, check-in and check-out date are required before an accommodation booking can be confirmed." });

        var hasConflict = await db.BookingRequests.AnyAsync(x =>
            x.Id != booking.Id &&
            x.RoomId == booking.RoomId &&
            x.Status == BookingStatuses.Confirmed &&
            x.CheckIn != null &&
            x.CheckOut != null &&
            x.CheckIn < booking.CheckOut &&
            x.CheckOut > booking.CheckIn);

        if (hasConflict)
            return Results.Conflict(new
            {
                message = $"{booking.Room?.Name ?? "This room"} already has a confirmed booking overlapping these dates."
            });
    }

    var previousStatus = booking.Status;
    booking.Status = normalizedStatus;
    booking.UpdatedAtUtc = DateTimeOffset.UtcNow;

    var adminId = GetAdminId(httpContext.User);
    var adminEmail = httpContext.User.FindFirstValue(ClaimTypes.Email) ??
                     httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Email);

    db.AuditLogs.Add(new AuditLog
    {
        AdminUserId = adminId,
        AdminEmail = adminEmail,
        Action = "BookingStatusChanged",
        EntityType = "BookingRequest",
        EntityId = booking.Id.ToString(),
        Details = $"{previousStatus} -> {booking.Status}"
    });

    await db.SaveChangesAsync();
    await transaction.CommitAsync();

    if (booking.Status is BookingStatuses.Confirmed or BookingStatuses.Declined or BookingStatuses.Cancelled)
    {
        try
        {
            await emailService.SendBookingStatusAsync(booking);
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Failed to send booking status email for {BookingId}", booking.Id);
        }
    }

    return Results.Ok(booking);
});

adminApi.MapGet("/users", async (AppDbContext db) =>
    Results.Ok(await db.AdminUsers.AsNoTracking()
        .OrderBy(x => x.DisplayName)
        .Select(x => new { x.Id, x.Email, x.DisplayName, x.IsActive, x.CreatedAtUtc })
        .ToListAsync()));

adminApi.MapPost("/users", async (
    CreateAdminRequest request,
    HttpContext httpContext,
    AppDbContext db,
    IPasswordHasher<AdminUser> hasher) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        return Results.BadRequest(new { message = "A valid email address is required." });

    if (string.IsNullOrWhiteSpace(request.DisplayName))
        return Results.BadRequest(new { message = "Display name is required." });

    if (request.Password.Length < 10)
        return Results.BadRequest(new { message = "Password must be at least 10 characters." });

    if (await db.AdminUsers.AnyAsync(x => x.Email == email))
        return Results.Conflict(new { message = "An admin with this email already exists." });

    var admin = new AdminUser
    {
        Email = email,
        DisplayName = request.DisplayName.Trim()
    };
    admin.PasswordHash = hasher.HashPassword(admin, request.Password);
    db.AdminUsers.Add(admin);

    db.AuditLogs.Add(new AuditLog
    {
        AdminUserId = GetAdminId(httpContext.User),
        AdminEmail = httpContext.User.FindFirstValue(ClaimTypes.Email),
        Action = "AdminCreated",
        EntityType = "AdminUser",
        EntityId = admin.Id.ToString(),
        Details = admin.Email
    });

    await db.SaveChangesAsync();
    return Results.Created($"/api/admin/users/{admin.Id}", new { admin.Id, admin.Email, admin.DisplayName, admin.IsActive });
});

adminApi.MapPatch("/users/{id:guid}/active", async (
    Guid id,
    SetAdminActiveRequest request,
    HttpContext httpContext,
    AppDbContext db) =>
{
    var currentAdminId = GetAdminId(httpContext.User);
    if (currentAdminId == id && !request.IsActive)
        return Results.BadRequest(new { message = "You cannot disable your own account." });

    var admin = await db.AdminUsers.SingleOrDefaultAsync(x => x.Id == id);
    if (admin is null)
        return Results.NotFound();

    admin.IsActive = request.IsActive;
    db.AuditLogs.Add(new AuditLog
    {
        AdminUserId = currentAdminId,
        AdminEmail = httpContext.User.FindFirstValue(ClaimTypes.Email),
        Action = request.IsActive ? "AdminEnabled" : "AdminDisabled",
        EntityType = "AdminUser",
        EntityId = admin.Id.ToString(),
        Details = admin.Email
    });

    await db.SaveChangesAsync();
    return Results.Ok(new { admin.Id, admin.Email, admin.DisplayName, admin.IsActive });
});

adminApi.MapPost("/change-password", async (
    ChangePasswordRequest request,
    HttpContext httpContext,
    AppDbContext db,
    IPasswordHasher<AdminUser> hasher) =>
{
    if (request.NewPassword.Length < 10)
        return Results.BadRequest(new { message = "New password must be at least 10 characters." });

    var adminId = GetAdminId(httpContext.User);
    if (adminId is null)
        return Results.Unauthorized();

    var admin = await db.AdminUsers.SingleOrDefaultAsync(x => x.Id == adminId.Value && x.IsActive);
    if (admin is null)
        return Results.Unauthorized();

    if (hasher.VerifyHashedPassword(admin, admin.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        return Results.BadRequest(new { message = "Current password is incorrect." });

    admin.PasswordHash = hasher.HashPassword(admin, request.NewPassword);
    db.AuditLogs.Add(new AuditLog
    {
        AdminUserId = admin.Id,
        AdminEmail = admin.Email,
        Action = "PasswordChanged",
        EntityType = "AdminUser",
        EntityId = admin.Id.ToString()
    });

    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Password changed successfully." });
});

adminApi.MapGet("/audit", async (int? take, AppDbContext db) =>
{
    var limit = Math.Clamp(take ?? 100, 1, 500);
    return Results.Ok(await db.AuditLogs.AsNoTracking()
        .OrderByDescending(x => x.CreatedAtUtc)
        .Take(limit)
        .ToListAsync());
});

adminApi.MapGet("/errors", async (int? take, AppDbContext db) =>
{
    var limit = Math.Clamp(take ?? 50, 1, 200);
    return Results.Ok(await db.ErrorLogs.AsNoTracking()
        .OrderByDescending(x => x.CreatedAtUtc)
        .Take(limit)
        .ToListAsync());
});

adminApi.MapPost("/backups", async (
    HttpContext httpContext,
    BackupService backupService,
    AppDbContext db) =>
{
    var backupPath = await backupService.CreateBackupAsync();
    db.AuditLogs.Add(new AuditLog
    {
        AdminUserId = GetAdminId(httpContext.User),
        AdminEmail = httpContext.User.FindFirstValue(ClaimTypes.Email),
        Action = "BackupCreated",
        EntityType = "Database",
        EntityId = "falaza.db",
        Details = backupPath
    });
    await db.SaveChangesAsync();

    return backupPath is null
        ? Results.BadRequest(new { message = "A file-based SQLite database is required for this backup endpoint." })
        : Results.Ok(new { message = "Backup created.", backupPath = Path.GetFileName(backupPath) });
});

app.Run();

static Dictionary<string, string[]> ValidateBooking(CreateBookingRequest request)
{
    var errors = new Dictionary<string, string[]>();

    if (string.IsNullOrWhiteSpace(request.GuestName))
        errors["guestName"] = ["Name is required."];

    if (string.IsNullOrWhiteSpace(request.Phone))
        errors["phone"] = ["Phone number is required."];

    if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        errors["email"] = ["A valid email address is required."];

    if (!EnquiryTypes.All.Contains(request.EnquiryType))
        errors["enquiryType"] = ["Please select a valid enquiry type."];

    if (request.Adults < 0 || request.Adults > 1000)
        errors["adults"] = ["Adults must be between 0 and 1000."];

    if (request.Children < 0 || request.Children > 1000)
        errors["children"] = ["Children must be between 0 and 1000."];

    if (request.EnquiryType.Equals(EnquiryTypes.Accommodation, StringComparison.OrdinalIgnoreCase))
    {
        if (!request.CheckIn.HasValue)
            errors["checkIn"] = ["Check-in is required for accommodation requests."];

        if (!request.CheckOut.HasValue)
            errors["checkOut"] = ["Check-out is required for accommodation requests."];
    }

    if (request.CheckIn.HasValue && request.CheckOut.HasValue && request.CheckOut <= request.CheckIn)
        errors["checkOut"] = ["Check-out must be after check-in."];

    return errors;
}

static string? Clean(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value.Trim();

static string HashToken(string token) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

static Guid? GetAdminId(ClaimsPrincipal user)
{
    var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ??
              user.FindFirstValue(JwtRegisteredClaimNames.Sub);
    return Guid.TryParse(raw, out var id) ? id : null;
}
