using ThrottlingTroll;

namespace IntegrationTests
{
    [TestClass]
    public class IntegrationTestsWithMemoryCache : IntegrationTestsBase
    {
        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            Init("http://localhost:17346/", new MemoryCacheCounterStore());
        }

        [ClassCleanup]
        public static async Task Cleanup()
        {
            await TearDown();
        }
    }
}