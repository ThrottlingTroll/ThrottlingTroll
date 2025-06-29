using Moq;

namespace ThrottlingTroll.Tests;

[TestClass]
public class ThrottlingTrollTests
{
    class TestRule : ThrottlingTrollRule
    {
        public LimitCheckResult Result = default!;

        protected internal override Task<LimitCheckResult> IsExceededAsync(IHttpRequestProxy request, long cost, ICounterStore store, string configName, Action<LogLevel, string> log)
        {
            return Task.FromResult(this.Result);
        }
    }

    [TestMethod]
    public async Task WithThrottlingTroll_LimitNotExceeded_Succeeds()
    {
        // Arrange

        var rule = new TestRule();
        rule.Result = new LimitCheckResult(1, rule, DateTimeOffset.Now.Millisecond, DateTimeOffset.Now.ToString());

        var troll = new ThrottlingTroll(new ThrottlingTrollConfig { Rules = new[] { rule } });
        string normalResult = DateTimeOffset.Now.ToString();

        // Act

        var result = await troll.WithThrottlingTroll(ctx => Task.FromResult(normalResult), async ctx =>
        {
            Assert.Fail("onLimitExceeded should not be called");
            return "should not be called";
        });

        // Assert

        Assert.AreEqual(normalResult, result);
    }

    [TestMethod]
    public async Task WithThrottlingTroll_LimitExceeded_InvokesOnLimitExceeded()
    {
        // Arrange

        var rule = new TestRule();
        rule.Result = new LimitCheckResult(-1, rule, DateTimeOffset.Now.Millisecond, DateTimeOffset.Now.ToString());

        var troll = new ThrottlingTroll(new ThrottlingTrollConfig { Rules = new[] { rule } });
        string limitExceededResult = DateTimeOffset.Now.ToString();

        // Act

        var result = await troll.WithThrottlingTroll(ctx => Task.FromResult("should not be returned"), async ctx => 
        {
            Assert.AreEqual(ctx.ExceededLimit.Rule, rule);

            return limitExceededResult;
        });

        // Assert

        Assert.AreEqual(limitExceededResult, result);
    }


    [TestMethod]
    public async Task WithThrottlingTroll_LimitExceeded_ContinuesAsNormal()
    {
        // Arrange

        var rule = new TestRule();
        rule.Result = new LimitCheckResult(-1, rule, DateTimeOffset.Now.Millisecond, DateTimeOffset.Now.ToString());

        var troll = new ThrottlingTroll(new ThrottlingTrollConfig { Rules = new[] { rule } });
        string normalResult = DateTimeOffset.Now.ToString();

        // Act

        var result = await troll.WithThrottlingTroll(ctx => Task.FromResult(normalResult), async ctx =>
        {
            Assert.AreEqual(ctx.ExceededLimit.Rule, rule);

            ctx.ShouldContinueAsNormal = true;

            return "should not be returned";
        });

        // Assert

        Assert.AreEqual(normalResult, result);
    }
}