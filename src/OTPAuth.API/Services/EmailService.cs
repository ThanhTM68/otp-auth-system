using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using OTPAuth.API.Configuration;
using System.Net.Sockets;

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
    private const string OtpEmailSubject = "Mã xác thực đăng nhập OTP";

    private readonly EmailOptions options = emailOptions.Value;
    private readonly string smtpPassword = NormalizeAppPassword(emailOptions.Value.Password);

    public async Task SendOtpAsync(OtpEmailMessage message, CancellationToken cancellationToken = default)
    {
        var configurationIssues = GetConfigurationIssues();
        if (configurationIssues.Count > 0)
        {
            logger.LogWarning(
                "OTP SMTP configuration is incomplete. MissingOrInvalid {Fields}; Host {Host}; Port {Port}; Recipient {Recipient}",
                string.Join(",", configurationIssues),
                options.Host,
                options.Port,
                MaskEmail(message.RecipientEmail));
            throw new EmailDeliveryException();
        }

        try
        {
            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(options.FromName, options.FromEmail));
            email.To.Add(MailboxAddress.Parse(message.RecipientEmail));
            email.Subject = OtpEmailSubject;
            email.Body = new TextPart("plain") { Text = BuildOtpBody(message) };

            using var smtpClient = new SmtpClient();
            await smtpClient.ConnectAsync(options.Host, options.Port, SecureSocketOptions.StartTls, cancellationToken);
            await smtpClient.AuthenticateAsync(options.Username.Trim(), smtpPassword, cancellationToken);
            await smtpClient.SendAsync(email, cancellationToken);
            await smtpClient.DisconnectAsync(true, cancellationToken);

            logger.LogInformation("OTP email delivery succeeded for recipient {Recipient}", MaskEmail(message.RecipientEmail));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AuthenticationException exception)
        {
            LogSmtpFailure("SMTP_AUTH_FAILED", exception, message.RecipientEmail);
            throw new EmailDeliveryException();
        }
        catch (SslHandshakeException exception)
        {
            LogSmtpFailure("TLS_FAILED", exception, message.RecipientEmail);
            throw new EmailDeliveryException();
        }
        catch (SocketException exception)
        {
            LogSmtpFailure("NETWORK_FAILED", exception, message.RecipientEmail);
            throw new EmailDeliveryException();
        }
        catch (SmtpCommandException exception)
        {
            var category = exception.ErrorCode is SmtpErrorCode.SenderNotAccepted or SmtpErrorCode.RecipientNotAccepted
                ? "MAILBOX_REJECTED"
                : "OTHER_SMTP_ERROR";
            LogSmtpFailure(category, exception, message.RecipientEmail);
            throw new EmailDeliveryException();
        }
        catch (Exception exception)
        {
            LogSmtpFailure("OTHER_SMTP_ERROR", exception, message.RecipientEmail);
            throw new EmailDeliveryException();
        }
    }

    public static string BuildOtpBody(OtpEmailMessage message) => $"""
        OTP Authentication System

        Mã xác thực đăng nhập của bạn là:

        {message.Otp}

        Mã có hiệu lực trong 3 phút (đến {message.ExpiresAt:HH:mm:ss} UTC).

        Không chia sẻ mã này cho người khác.
        Nếu bạn không thực hiện yêu cầu đăng nhập này, hãy bỏ qua email.
        """;

    private List<string> GetConfigurationIssues()
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            issues.Add(nameof(EmailOptions.Host));
        }
        if (options.Port is <= 0 or > 65535)
        {
            issues.Add(nameof(EmailOptions.Port));
        }
        if (string.IsNullOrWhiteSpace(options.Username))
        {
            issues.Add(nameof(EmailOptions.Username));
        }
        if (smtpPassword.Length != 16)
        {
            issues.Add(nameof(EmailOptions.Password));
        }
        if (string.IsNullOrWhiteSpace(options.FromEmail))
        {
            issues.Add(nameof(EmailOptions.FromEmail));
        }
        if (!options.EnableSsl)
        {
            issues.Add(nameof(EmailOptions.EnableSsl));
        }

        return issues;
    }

    private static string NormalizeAppPassword(string password) =>
        string.Concat(password.Where(character => !char.IsWhiteSpace(character)));

    private void LogSmtpFailure(string category, Exception exception, string recipientEmail) =>
        logger.LogWarning(
            "OTP SMTP delivery failed. Category {Category}; ExceptionType {ExceptionType}; Host {Host}; Port {Port}; Recipient {Recipient}",
            category,
            exception.GetType().Name,
            options.Host,
            options.Port,
            MaskEmail(recipientEmail));

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
