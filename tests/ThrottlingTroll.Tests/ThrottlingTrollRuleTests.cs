using Microsoft.Extensions.Primitives;
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

        Assert.AreEqual("|Ingress|w/iDWn0W6f2VKmmXWEzN/m0hF3dOEO3OMoSrWV9gXgc=", key);
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

        Assert.AreEqual("|Egress|w/iDWn0W6f2VKmmXWEzN/m0hF3dOEO3OMoSrWV9gXgc=", key);
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

        Assert.AreEqual("test-config-name|Ingress|0Rz5p6PEVLqBcanRSYWKbBuJIPeuQl65kKuwm4PyrXA=", key);
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

        Assert.AreEqual("test-config-name|Ingress|Ll8oXPxiLzc3o7G72jNYvo1sm1UDX40/0y7jcZMSMOw=", key);
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

        string expectedKey = $"|Ingress|{this.GetHash($"<>|<>|<>|<>|<>|<FixedWindowRateLimitMethod(1,2)>|<{identityId.ToString()}>")}";
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

        string expectedKey = $"|Ingress|{this.GetHash($"<>|<{rule.UriPattern}>|<>|<>|<>|<FixedWindowRateLimitMethod(1,2)>|<{identityId.ToString()}>")}";
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

        Assert.AreEqual("|Ingress|ZWcgAiEl2X3dv4juIuS5n2lL229y+OaDxnoA9GiH1Nc=", key);
    }

    [TestMethod]
    public void GetUniqueCacheKey_IdentityIdHeaderValuePlaceholderReturnsGuid_ReturnsCacheKey()
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

            HeaderName = "my-client-id-header",
            HeaderValuePattern = "client-id-(?<ThrottlingTrollIdentityId>[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12})"
        };

        var requestMock = new Mock<IIncomingHttpRequestProxy>();
        requestMock.SetupGet(r => r.Headers).Returns(new Dictionary<string, StringValues>
        {
            ["my-client-id-header"] = $"client-id-{identityId}" }
        );

        // Act

        string key = rule.GetUniqueCacheKey(requestMock.Object, "");

        // Assert

        string expectedKey = $"|Ingress|{this.GetHash($"<>|<>|<my-client-id-header>|<>|<{rule.HeaderValuePattern}>|<FixedWindowRateLimitMethod(1,2)>|<{identityId.ToString()}>")}";
        Assert.AreEqual(expectedKey, key);
    }


    [TestMethod]
    public void GetUniqueCacheKey_IdentityIdHeaderValuePlaceholderDoesNotMatch_ReturnsCacheKey()
    {
        // Arrange

        var rule = new ThrottlingTrollRule()
        {
            LimitMethod = new FixedWindowRateLimitMethod()
            {
                PermitLimit = 1,
                IntervalInSeconds = 2
            },

            HeaderName = "my-client-id-header",
            HeaderValuePattern = "client-id-(?<ThrottlingTrollIdentityId>[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12})"
        };

        var requestMock = new Mock<IIncomingHttpRequestProxy>();
        requestMock.SetupGet(r => r.Headers).Returns(new Dictionary<string, StringValues>
        {
            ["my-client-id-header"] = $"client-id-not-a-guid"
        }
        );

        // Act

        string key = rule.GetUniqueCacheKey(requestMock.Object, "");

        // Assert

        Assert.AreEqual("|Ingress|DKtfpNDejytr8r3H/KYg0zcimNlMNYGDbZRzQV79lWw=", key);
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