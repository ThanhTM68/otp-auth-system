using OTPAuth.API.Services;

namespace OTPAuth.Tests;

internal sealed class FakeEmailService(bool shouldFail = false) : IEmailService
{
    public int CallCount { get; private set; }
    public List<OtpEmailMessage> Messages { get; } = [];

    public Task SendOtpAsync(OtpEmailMessage message, CancellationToken cancellationToken = default)
    {
        CallCount++;
        if (shouldFail)
        {
            throw new EmailDeliveryException();
        }

        Messages.Add(message);
        return Task.CompletedTask;
    }
}
