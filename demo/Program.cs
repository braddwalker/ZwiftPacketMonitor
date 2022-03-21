using System;
using SharpPcap;
using SharpPcap.LibPcap;
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

            // added to allow logging level configuration, change MinLevel to LogLevel.Debug to see internal traces.
            serviceCollection.AddLogging(configure => configure.AddConsole())
                .Configure<LoggerFilterOptions>(configure => configure.MinLevel = LogLevel.Information);

            serviceCollection.AddZwiftPacketMonitoring();

            var serviceProvider = serviceCollection.BuildServiceProvider(); 

            var logger = serviceProvider.GetService<ILogger<Program>>();
            var monitor = serviceProvider.GetService<Monitor>();

            /*monitor.IncomingPlayerEvent += (s, e) =>
            {
                logger.LogInformation($"INCOMING: {e.PlayerState}");
            };
            monitor.OutgoingPlayerEvent += (s, e) =>
            {
                logger.LogInformation($"OUTGOING: {e.PlayerState}");
            };*/
            monitor.IncomingSocialPlayerActionEvent += (s, e) =>
            {
                logger.LogInformation($"SPA: {e.SocialPlayerAction}");
            };
            /*monitor.IncomingSegmentResultEvent += (s, e) =>
            {
                logger.LogInformation($"SegmentResult: {e.SegmentResult}");
            };*/
            monitor.IncomingRideOnGivenEvent += (s, e) =>
            {
                logger.LogInformation($"RIDEON: {e.RideOn}");
            };
            /*monitor.IncomingEventProtobufEvent += (s, e) =>
            {
                logger.LogInformation($"EventProtobuf: {e.EventProtobuf}");
            };*/
            monitor.IncomingPlayerJoinedEvent += (s, e) =>
            {
                logger.LogInformation($"PlayerJoinedEvent: {e.PlayerJoinedEvent}");
            };
            monitor.IncomingPlayerLeftEvent += (s, e) =>
            {
                logger.LogInformation($"PlayerLeftEvent: {e.PlayerLeftEvent}");
            };
            /*monitor.IncomingPlayerLeftWorldEvent += (s, e) =>
            {
                logger.LogInformation($"PlayerLeftWorld: {e.WaType}: {e.PlayerLeftWorld}");
            };*/
            monitor.IncomingEventInviteEvent += (s, e) =>
            {
                logger.LogInformation($"EventInvite: {e.EventInviteProto}");
            };
            /*monitor.IncomingNotableEvent += (s, e) =>
            {
                logger.LogInformation($"Notable: PlayerId: {e.PlayerId} Hex: {e.NotableHexStr}");
            };
            monitor.IncomingGroupEvent += (s, e) =>
            {
                logger.LogInformation($"GroupEvent {e.GeKindStr}: PlayerId: {e.PlayerId} Hex: {e.GeHexStr}");
            };*/
            monitor.IncomingBikeActionEvent += (s, e) =>
            {
                logger.LogInformation($"BikeActionEvent {e.BikeAction}: PlayerId: {e.PlayerId} Hex: {e.BaHexStr}");
            };
            monitor.IncomingNoPayloadWaEvent += (s, e) =>
            {
                logger.LogInformation($"NoPayloadWa: {e.WaType}");
            };
            monitor.IncomingMessageWaEvent += (s, e) =>
            {
                logger.LogInformation($"Message: {e.WaType}: {e.Message}");
            };
            monitor.IncomingFloatTimeWaEvent += (s, e) =>
            {
                logger.LogInformation($"FloatTimeWa: {e.WaType}: {e.FloatTime}");
            };
            /*monitor.IncomingFlagWaEvent += (s, e) =>
            {
                logger.LogInformation($"FlagWa: {e.FlagWaInfo}");
            };*/

            // Print SharpPcap version
            var ver = Pcap.SharpPcapVersion;
            Console.WriteLine("SharpPcap {0}, ZwiftPacketMonitor.Demo", ver);

            // Retrieve the device list
            var devices = LibPcapLiveDeviceList.Instance;

            // If no devices were found print an error
            if (devices.Count < 1)
            {
                Console.WriteLine("No devices were found on this machine");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("The following devices are available on this machine:");
            Console.WriteLine("----------------------------------------------------");
            Console.WriteLine();

            int i = 0;

            // Print out the devices
            foreach (var dev in devices)
            {
                /* Description */
                Console.WriteLine("{0}) {1} {2} [{3}]", i, dev.Name, dev.Description, dev.Interface.FriendlyName);
                i++;
            }

            Console.WriteLine();
            Console.Write("-- Please choose a device to capture: ");
            i = int.Parse(Console.ReadLine());

            // network interface name or IP address (windows only)
            _ = monitor.StartCaptureAsync(devices[i].Interface.Name); // not waiting to complete as it doesn't until StopCaptureAsync is called, instead wait for a keypress

            // waiting for a keypress to shutdown clean
            Console.WriteLine();
            Console.WriteLine("Press ctrl-c to quit.");
            Console.ReadLine();
          
            // Stop monitoring
            monitor.StopCaptureAsync().Wait();
        }
    }
}
