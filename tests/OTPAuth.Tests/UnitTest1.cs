namespace OTPAuth.Tests;

public class ProjectInitializationTests
{
    [Fact]
    public void ApiProject_IsReferenced()
    {
        Assert.Equal("OTPAuth.API", typeof(global::Program).Assembly.GetName().Name);
    }
}
