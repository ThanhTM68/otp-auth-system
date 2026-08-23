using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using OTPAuth.API.Configuration;

namespace OTPAuth.API.Services;

public sealed record OtpEmailMessage(string RecipientEmail, string Otp, DateTimeOffset ExpiresAt);

public interface IEmailService
{
    Task SendOtpAsync(OtpEmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException() : base("OTP email delivery failed.")
    {
    }
}

public sealed class EmailService(
    IOptions<EmailOptions> emailOptions,
    ILogger<EmailService> logger) : IEmailService
{
    private readonly EmailOptions options = emailOptions.Value;

    public async Task SendOtpAsync(OtpEmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            logger.LogWarning("OTP email delivery is unavailable for recipient {Recipient}", MaskEmail(message.RecipientEmail));
            throw new EmailDeliveryException();
        }

        try
        {
            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(options.FromName, options.FromEmail));
            email.To.Add(MailboxAddress.Parse(message.RecipientEmail));
            email.Subject = "Mã xác thực đăng nhập";
            email.Body = new TextPart("plain") { Text = BuildOtpBody(message) };

            using var smtpClient = new SmtpClient();
            await smtpClient.ConnectAsync(options.Host, options.Port, SecureSocketOptions.StartTls, cancellationToken);
            await smtpClient.AuthenticateAsync(options.Username, options.Password, cancellationToken);
            await smtpClient.SendAsync(email, cancellationToken);
            await smtpClient.DisconnectAsync(true, cancellationToken);

            logger.LogInformation("OTP email delivery succeeded for recipient {Recipient}", MaskEmail(message.RecipientEmail));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogWarning("OTP email delivery failed for recipient {Recipient}", MaskEmail(message.RecipientEmail));
            throw new EmailDeliveryException();
        }
    }

    public static string BuildOtpBody(OtpEmailMessage message) => $"""
        OTP Authentication System

        Mã xác thực đăng nhập của bạn là:

        {message.Otp}

        Mã hết hạn lúc {message.ExpiresAt:HH:mm:ss} UTC.

        Không chia sẻ mã này cho người khác.
        Nếu bạn không thực hiện yêu cầu đăng nhập này, hãy bỏ qua email.
        """;

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(options.Host) &&
        options.Port is > 0 and <= 65535 &&
        !string.IsNullOrWhiteSpace(options.Username) &&
        !string.IsNullOrWhiteSpace(options.Password) &&
        !string.IsNullOrWhiteSpace(options.FromEmail) &&
        options.EnableSsl;

    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1)
        {
            return "***";
        }

        return $"{email[0]}***{email[atIndex..]}";
    }
}
