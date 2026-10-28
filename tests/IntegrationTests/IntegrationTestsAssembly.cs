using OtelTap;

namespace IntegrationTests
{
    [TestClass]
    public class IntegrationTestsAssembly
    {
        internal static readonly Uri TelemetryEndpoint = new Uri("http://127.0.0.1:17446/");

        /// <summary>
        /// One shared per-assembly OtelTap instance,
        /// to ensure child test classes do not create competing instances.
        /// </summary>
        internal static OtelTapHttpProtobufReceiver? OtelTap { get; private set; }

        [AssemblyInitialize]
        public static void Setup(TestContext context)
        {
            OtelTap = OtelTapHttpProtobufReceiver.Start(new OtelTapHttpProtobufReceiverSettings(
                HttpPort: TelemetryEndpoint.Port,
                PrintTracesAsNdjson: true,
                PrintLogsAsNdjson: true,
                PrintMetricsAsNdjson: true,
                Log: (msg, ex) => Console.Error.WriteLine($"OtelTap failed: {msg} {ex}")));
        }

        [AssemblyCleanup]
        public static void Cleanup()
        {
            // Keep receiving telemetry until all class cleanup methods have finished.
            OtelTap?.Dispose();
        }
    }
}
