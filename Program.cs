using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FalazaLodge.Api;
using FalazaLodge.Api.Data;
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
    {
        jwtKey = "dev-only-change-this-falaza-jwt-key-2026-at-least-32-chars";
    }
    else
    {
        throw new InvalidOperationException("JWT key is required. Set Jwt__Key or FALAZA_JWT_KEY.");
    }
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
    await db.Database.EnsureCreatedAsync();

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
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    service = "Falaza Lodge Jozini API",
    status = "online"
}));

app.MapPost("/api/bookings", async (CreateBookingRequest request, AppDbContext db) =>
{
    var errors = ValidateBooking(request);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var booking = new BookingRequest
    {
        EnquiryType = request.EnquiryType.Trim(),
        CheckIn = request.CheckIn,
        CheckOut = request.CheckOut,
        Adults = request.Adults,
        Children = request.Children,
        RoomPreference = Clean(request.RoomPreference),
        GuestName = request.GuestName.Trim(),
        Phone = request.Phone.Trim(),
        Email = request.Email.Trim().ToLowerInvariant(),
        Details = Clean(request.Details),
        Status = BookingStatuses.New
    };

    db.BookingRequests.Add(booking);
    await db.SaveChangesAsync();

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
        new Claim(JwtRegisteredClaimNames.Email, admin.Email),
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

    return Results.Ok(new
    {
        token = new JwtSecurityTokenHandler().WriteToken(token),
        expiresAtUtc = expires,
        admin = new { admin.Email, admin.DisplayName }
    });
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

    var recent = await db.BookingRequests
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

adminApi.MapGet("/bookings", async (
    string? status,
    string? type,
    string? search,
    AppDbContext db) =>
{
    var query = db.BookingRequests.AsNoTracking().AsQueryable();

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
        .ToListAsync();

    return Results.Ok(results);
});

adminApi.MapGet("/bookings/{id:guid}", async (Guid id, AppDbContext db) =>
{
    var booking = await db.BookingRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
    return booking is null ? Results.NotFound() : Results.Ok(booking);
});

adminApi.MapPatch("/bookings/{id:guid}/status", async (
    Guid id,
    UpdateBookingStatusRequest request,
    AppDbContext db) =>
{
    if (!BookingStatuses.All.Contains(request.Status))
        return Results.BadRequest(new { message = "Invalid booking status." });

    var booking = await db.BookingRequests.SingleOrDefaultAsync(x => x.Id == id);
    if (booking is null)
        return Results.NotFound();

    booking.Status = BookingStatuses.All.First(x => x.Equals(request.Status, StringComparison.OrdinalIgnoreCase));
    booking.UpdatedAtUtc = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(booking);
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

    if (request.CheckIn.HasValue && request.CheckOut.HasValue && request.CheckOut < request.CheckIn)
        errors["checkOut"] = ["Check-out must be on or after check-in."];

    return errors;
}

static string? Clean(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value.Trim();
