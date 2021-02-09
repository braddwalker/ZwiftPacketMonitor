# ZwiftPacketMonitor
This project implements a TCP and UDP packet monitor for the Zwift cycling simulator. It listens for packets on a specific port of a local network adapter, and when found, deserializes the payload and dispatches events that can be consumed by the caller.

**NOTE**: Because this utilizes a network packet capture to intercept the UDP packets, your system may require this code to run using elevated privileges.

## Prerequisites
* Packet capture relies on [SharpPcap](https://github.com/chmorgan/sharppcap) and requires the installation of libpcap (Linux), Npcap (Windows) or similar packet capture library.

## Usage
```c#
using System;
using SharpPcap;
using ZwiftPacketMonitor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
//The package Microsoft.Extensions.Logging.Console needs to be added

namespace ZwiftPacketMonitorDemo
{
    class Program
    {
        static void Main(string[] args)
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.AddLogging(configure => configure.AddConsole())
                .Configure<LoggerFilterOptions>(configure => configure.MinLevel = LogLevel.Debug) // added to allow logging level configuration
                ;

            RegistrationExtensions.AddZwiftPacketMonitoring(serviceCollection);

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
```

## Credit
This project is a .NET port of the [zwift-packet-monitor](https://github.com/jeroni7100/zwift-packet-monitor) project and borrows heavily from its packet handling and protobuf implementation.

