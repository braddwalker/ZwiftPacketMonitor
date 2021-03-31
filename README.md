# ZwiftPacketMonitor
This project implements a TCP and UDP packet monitor for the Zwift cycling simulator. It listens for packets on a specific port of a local network adapter, and when found, deserializes the payload and dispatches events that can be consumed by the caller.

**NOTE**: Because this utilizes a network packet capture to intercept the UDP packets, your system may require this code to run using elevated privileges.

## Prerequisites
* Packet capture relies on [SharpPcap](https://github.com/chmorgan/sharppcap) and requires the installation of libpcap (Linux), Npcap (Windows) or similar packet capture library.

## Usage
See the included ZwiftPacketMonitor.Demo project for a complete working example.

```c#
    var serviceCollection = new ServiceCollection();
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

    // network interface name or IP address
    monitor.StartCaptureAsync("en0").Wait();
    
    // This won't get called until the above Wait finishes
    monitor.StopCaptureAsync().Wait();
```

## Credit
This project is a .NET port of the [zwift-packet-monitor](https://github.com/jeroni7100/zwift-packet-monitor) project and borrows heavily from its packet handling and protobuf implementation.

