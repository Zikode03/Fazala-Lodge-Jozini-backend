using System.Net;
using System.Net.Mail;

namespace FalazaLodge.Api.Services;

public interface IEmailService
{
    Task SendNewBookingNotificationAsync(BookingRequest booking, CancellationToken cancellationToken = default);
    Task SendBookingStatusAsync(BookingRequest booking, CancellationToken cancellationToken = default);
    Task SendPasswordResetAsync(AdminUser admin, string resetUrl, CancellationToken cancellationToken = default);
}

public sealed class EmailService(IConfiguration configuration, ILogger<EmailService> logger) : IEmailService
{
    public async Task SendNewBookingNotificationAsync(BookingRequest booking, CancellationToken cancellationToken = default)
    {
        var recipients = configuration.GetSection("Email:AdminRecipients").Get<string[]>() ?? [];
        if (recipients.Length == 0)
        {
            logger.LogWarning("No Email:AdminRecipients configured; new booking email skipped for {BookingId}", booking.Id);
            return;
        }

        var subject = $"New Falaza {booking.EnquiryType} request - {booking.GuestName}";
        var body = $"""
        A new request has been submitted.

        Guest: {booking.GuestName}
        Type: {booking.EnquiryType}
        Check-in / event date: {booking.CheckIn}
        Check-out: {booking.CheckOut}
        Room preference: {booking.RoomPreference ?? "None"}
        Adults / guests: {booking.Adults}
        Children: {booking.Children}
        Phone: {booking.Phone}
        Email: {booking.Email}

        Details:
        {booking.Details ?? "None"}

        Booking ID: {booking.Id}
        """;

        foreach (var recipient in recipients)
            await SendAsync(recipient, subject, body, cancellationToken);
    }

    public Task SendBookingStatusAsync(BookingRequest booking, CancellationToken cancellationToken = default)
    {
        var subject = $"Falaza Lodge booking request - {booking.Status}";
        var dates = booking.CheckIn.HasValue
            ? booking.CheckOut.HasValue ? $"{booking.CheckIn:yyyy-MM-dd} to {booking.CheckOut:yyyy-MM-dd}" : booking.CheckIn.Value.ToString("yyyy-MM-dd")
            : "Date to be confirmed";

        var body = $"""
        Dear {booking.GuestName},

        Your Falaza Lodge request has been updated.

        Status: {booking.Status}
        Type: {booking.EnquiryType}
        Date(s): {dates}
        Room / venue preference: {booking.RoomPreference ?? "Not specified"}

        {(booking.Status == BookingStatuses.Confirmed ? "Your request has been confirmed. Falaza Lodge will contact you with any remaining arrangements." : booking.Status == BookingStatuses.Declined ? "Unfortunately, Falaza Lodge cannot confirm this request as submitted. Please contact the lodge if you would like to discuss alternatives." : "Your request has been updated.")}

        Falaza Lodge Jozini
        +27 61 594 5458
        """;

        return SendAsync(booking.Email, subject, body, cancellationToken);
    }

    public Task SendPasswordResetAsync(AdminUser admin, string resetUrl, CancellationToken cancellationToken = default)
    {
        var subject = "Falaza Lodge admin password reset";
        var body = $"""
        Hello {admin.DisplayName},

        A password reset was requested for your Falaza Lodge admin account.

        Use this link within 30 minutes:
        {resetUrl}

        If you did not request this, you can ignore this email.
        """;

        return SendAsync(admin.Email, subject, body, cancellationToken);
    }

    private async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        var host = configuration["Email:SmtpHost"];
        var from = configuration["Email:FromAddress"];

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
        {
            logger.LogWarning("SMTP is not configured; email to {Recipient} with subject {Subject} was skipped.", recipient, subject);
            return;
        }

        var port = configuration.GetValue("Email:SmtpPort", 587);
        var enableSsl = configuration.GetValue("Email:EnableSsl", true);
        var username = configuration["Email:Username"];
        var password = configuration["Email:Password"];

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };

        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new NetworkCredential(username, password);

        using var message = new MailMessage(from, recipient, subject, body);
        await client.SendMailAsync(message, cancellationToken);
        logger.LogInformation("Email sent to {Recipient}: {Subject}", recipient, subject);
    }
}
