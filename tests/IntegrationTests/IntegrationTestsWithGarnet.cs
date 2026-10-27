using Garnet;
using Garnet.server;
using StackExchange.Redis;
using ThrottlingTroll.CounterStores.Redis;

namespace IntegrationTests
{
    [TestClass]
    public class IntegrationTestsWithGarnet : IntegrationTestsBase
    {
        static GarnetServer GarnetServer;

        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            GarnetServerOptions garnetOptions = new()
            {
                EnableLua = true,
                LuaOptions = new LuaOptions()
            };

            GarnetServer = new GarnetServer(garnetOptions);

            GarnetServer.Start();

            Init("http://localhost:17347/", new RedisCounterStore(ConnectionMultiplexer.Connect("localhost:6379")));
        }

        [ClassCleanup]
        public static async Task Cleanup()
        {
            try
            {
                await TearDown();
            }
            finally
            {
                GarnetServer?.Dispose();
            }
        }
    }
}