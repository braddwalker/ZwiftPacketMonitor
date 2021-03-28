using System;
using SharpPcap;
using ZwiftPacketMonitor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ZwiftPacketMonitorDemo
{
    class Program
    {
        static void Main(string[] args)
        {
            var serviceCollection = new ServiceCollection();

            // added to allow logging level configuration
            serviceCollection.AddLogging(configure => configure.AddConsole())
                .Configure<LoggerFilterOptions>(configure => configure.MinLevel = LogLevel.Debug);

            serviceCollection.AddZwiftPacketMonitoring();

            var serviceProvider = serviceCollection.BuildServiceProvider(); 

            var logger = serviceProvider.GetService<ILogger<Program>>();
            var monitor = serviceProvider.GetService<Monitor>();

            monitor.IncomingPlayerEvent += (s, e) => {
                logger.LogInformation($"INCOMING: {e.PlayerState}");
            };
            monitor.OutgoingPlayerEvent += (s, e) => {
                logger.LogInformation($"OUTGOING: {e.PlayerState}");
            };
            monitor.IncomingChatMessageEvent += (s, e) => {
                logger.LogInformation($"CHAT: {e.Message}");
            };
            monitor.IncomingPlayerEnteredWorldEvent += (s, e) => {
                logger.LogInformation($"WORLD: {e.PlayerUpdate}");
            };
            monitor.IncomingRideOnGivenEvent += (s, e) => {
                logger.LogInformation($"RIDEON: {e.RideOn}");
            };

            // network interface name or IP address (windows only)
            monitor.StartCaptureAsync("en0").Wait();
          
            // This won't get called until the above Wait finishes
            monitor.StopCaptureAsync().Wait();
        }
    }
}
