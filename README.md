# ZwiftPacketMonitor
This project implements a UDP packet monitor for the Zwift cycling simulator. It listens for packets on a specific port of a local network adapter, and when found, deserializes the payload and dispatches events that can be consumed by the caller.

This project is a .Net port of the Node zwift-packet-monitor project (https://github.com/jeroni7100/zwift-packet-monitor).

**NOTE**: Because this utilizes a network packet capture to intercept the UDP packets, your system may require this code to run using elevated privileges.

## Usage
---
```c#
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
            serviceCollection.AddLogging(configure => configure.AddConsole())                    
                .AddTransient<Monitor>();
            var serviceProvider = serviceCollection.BuildServiceProvider(); 

            var logger = serviceProvider.GetService<ILogger<Program>>();

            var monitor = serviceProvider.GetService<Monitor>();
            monitor.IncomingPlayerEvent += (s, e) => {
                logger.LogInformation($"INCOMING: {e.PlayerState}");
            };
            monitor.OutgoingPlayerEvent += (s, e) => {
                logger.LogInformation($"OUTGOING: {e.PlayerState}");
            };

            monitor.StartCaptureAsync("en0").Wait();
          
            // This won't get called until the above Wait finishes
            monitor.StopCaptureAsync().Wait();
        }
    }
}
```
