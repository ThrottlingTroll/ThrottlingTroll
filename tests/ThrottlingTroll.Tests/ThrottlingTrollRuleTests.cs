using Moq;
using System.Security.Cryptography;
using System.Text;

namespace ThrottlingTroll.Tests;

[TestClass]
public class ThrottlingTrollRuleTests
{
    [TestMethod]
    public void GetUniqueCacheKey_Ingress_JustEmptyLimitMethod_ReturnsCacheKey()
    {
        // Arrange

        var rule = new ThrottlingTrollRule()
        {
            LimitMethod = new FixedWindowRateLimitMethod()
        };

        var requestMock = new Mock<IIncomingHttpRequestProxy>();

        // Act

        string key = rule.GetUniqueCacheKey(requestMock.Object, "");

        // Assert

        Assert.AreEqual("|Ingress|yy/TSadjPTeFwGf9PoApQrjHIFRtr6ZpKtE6oFf3tGA=", key);
    }

    [TestMethod]
    public void GetUniqueCacheKey_Egress_JustEmptyLimitMethod_ReturnsCacheKey()
    {
        // Arrange

        var rule = new ThrottlingTrollRule()
        {
            LimitMethod = new FixedWindowRateLimitMethod()
        };

        var requestMock = new Mock<IOutgoingHttpRequestProxy>();

        // Act

        string key = rule.GetUniqueCacheKey(requestMock.Object, "");

        // Assert

        Assert.AreEqual("|Egress|yy/TSadjPTeFwGf9PoApQrjHIFRtr6ZpKtE6oFf3tGA=", key);
    }

    [TestMethod]
    public void GetUniqueCacheKey_FullyConfiguredRule_ReturnsCacheKey()
    {
        // Arrange

        var rule = new ThrottlingTrollRule()
        {
            LimitMethod = new SlidingWindowRateLimitMethod()
            {
                PermitLimit = 1,
                IntervalInSeconds = 2,
                NumOfBuckets = 3
            },

            Method = "POST,DELETE",
            UriPattern = "ab/cd/ef",
            HeaderName = "my-header-name",
            HeaderValue = "my-header-value123",
        };

        var requestMock = new Mock<IIncomingHttpRequestProxy>();

        // Act

        string key = rule.GetUniqueCacheKey(requestMock.Object, "test-config-name");

        // Assert

        Assert.AreEqual("test-config-name|Ingress|rrfpjbZVnNVjL2D21kpiW/cDPS9zo5iQ4pKXBjVRG3U=", key);
    }

    [TestMethod]
    public void GetUniqueCacheKey_IdentityIdExtractorReturnsNull_ReturnsCacheKey()
    {
        // Arrange

        var rule = new ThrottlingTrollRule()
        {
            LimitMethod = new FixedWindowRateLimitMethod()
            {
                PermitLimit = 1,
                IntervalInSeconds = 2
            },

            IdentityIdExtractor = r => null
        };

        var requestMock = new Mock<IIncomingHttpRequestProxy>();

        // Act

        string key = rule.GetUniqueCacheKey(requestMock.Object, "test-config-name");

        // Assert

        Assert.AreEqual("test-config-name|Ingress|87WmZq3MsWEQ8mrPmLPw3MbC90Ax3N74yTzjXvQMHXw=", key);
    }

    [TestMethod]
    public void GetUniqueCacheKey_IdentityIdExtractorReturnsGuid_ReturnsCacheKey()
    {
        // Arrange

        Guid identityId = Guid.NewGuid();

        var rule = new ThrottlingTrollRule()
        {
            LimitMethod = new FixedWindowRateLimitMethod()
            {
                PermitLimit = 1,
                IntervalInSeconds = 2
            },

            IdentityIdExtractor = r => identityId.ToString()
        };

        var requestMock = new Mock<IIncomingHttpRequestProxy>();

        // Act

        string key = rule.GetUniqueCacheKey(requestMock.Object, "");

        // Assert

        string expectedKey = $"|Ingress|{this.GetHash($"<>|<>|<>|<>|<FixedWindowRateLimitMethod(1,2)>|<{identityId.ToString()}>")}";
        Assert.AreEqual(expectedKey, key);
    }

    [TestMethod]
    public void GetUniqueCacheKey_IdentityIdPlaceholderReturnsGuid_ReturnsCacheKey()
    {
        // Arrange

        Guid identityId = Guid.NewGuid();

        var rule = new ThrottlingTrollRule()
        {
            LimitMethod = new FixedWindowRateLimitMethod()
            {
                PermitLimit = 1,
                IntervalInSeconds = 2
            },

            UriPattern = "/my/path\\?client-id=(?<ThrottlingTrollIdentityId>[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12})"
        };

        var requestMock = new Mock<IIncomingHttpRequestProxy>();
        requestMock.SetupGet(r => r.Uri).Returns($"/my/path?client-id={identityId}");

        // Act

        string key = rule.GetUniqueCacheKey(requestMock.Object, "");

        // Assert

        string expectedKey = $"|Ingress|{this.GetHash($"<>|<{rule.UriPattern}>|<>|<>|<FixedWindowRateLimitMethod(1,2)>|<{identityId.ToString()}>")}";
        Assert.AreEqual(expectedKey, key);
    }

    [TestMethod]
    public void GetUniqueCacheKey_IdentityIdPlaceholderDoesNotMatch_ReturnsCacheKey()
    {
        // Arrange

        var rule = new ThrottlingTrollRule()
        {
            LimitMethod = new FixedWindowRateLimitMethod()
            {
                PermitLimit = 1,
                IntervalInSeconds = 2
            },

            UriPattern = "/my/path\\?client-id=(?<ThrottlingTrollIdentityId>[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12})"
        };

        var requestMock = new Mock<IIncomingHttpRequestProxy>();
        requestMock.SetupGet(r => r.Uri).Returns($"/my/path?client-id=not-a-guid");

        // Act

        string key = rule.GetUniqueCacheKey(requestMock.Object, "");

        // Assert

        Assert.AreEqual("|Ingress|k3Na1M+rEKOYKqso36NxOQ+zoxZXziB70OJ9PsqMi6Q=", key);
    }

    private string GetHash(string str)
    {
        // HashAlgorithm instances should NOT be reused
        using (var sha256 = SHA256.Create())
        {
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(str));

            return Convert.ToBase64String(bytes);
        }
    }
}