namespace FalazaLodge.Api;

public static class BookingStatuses
{
    public const string New = "New";
    public const string Confirmed = "Confirmed";
    public const string Declined = "Declined";
    public const string Cancelled = "Cancelled";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        New, Confirmed, Declined, Cancelled
    };
}

public static class EnquiryTypes
{
    public const string Accommodation = "Accommodation";
    public const string Conference = "Conference / meeting";
    public const string Wedding = "Wedding / celebration";
    public const string PrivateEvent = "Private function / event";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Accommodation, Conference, Wedding, PrivateEvent
    };
}

public sealed class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int Capacity { get; set; } = 2;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<BookingRequest> Bookings { get; set; } = new List<BookingRequest>();
}

public sealed class BookingRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EnquiryType { get; set; } = EnquiryTypes.Accommodation;
    public DateOnly? CheckIn { get; set; }
    public DateOnly? CheckOut { get; set; }
    public int Adults { get; set; } = 1;
    public int Children { get; set; }
    public Guid? RoomId { get; set; }
    public Room? Room { get; set; }
    public string? RoomPreference { get; set; }
    public string GuestName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Details { get; set; }
    public string Status { get; set; } = BookingStatuses.New;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AdminUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string DisplayName { get; set; } = "Falaza Lodge Admin";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public Guid? AdminUserId { get; set; }
    public string? AdminEmail { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string? Details { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AdminUserId { get; set; }
    public AdminUser AdminUser { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ErrorLog
{
    public long Id { get; set; }
    public string TraceId { get; set; } = "";
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string ExceptionType { get; set; } = "";
    public string Message { get; set; } = "";
    public string? StackTrace { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record CreateBookingRequest(
    string EnquiryType,
    DateOnly? CheckIn,
    DateOnly? CheckOut,
    int Adults,
    int Children,
    string? RoomPreference,
    string GuestName,
    string Phone,
    string Email,
    string? Details);

public sealed record LoginRequest(string Email, string Password);
public sealed record UpdateBookingStatusRequest(string Status);
public sealed record CreateAdminRequest(string Email, string DisplayName, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record SetAdminActiveRequest(bool IsActive);
public sealed record RequestPasswordResetRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
