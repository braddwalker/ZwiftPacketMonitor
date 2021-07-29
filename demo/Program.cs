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

            // added to allow logging level configuration
            serviceCollection.AddLogging(configure => configure.AddConsole())
                .Configure<LoggerFilterOptions>(configure => configure.MinLevel = LogLevel.Information);

            serviceCollection.AddZwiftPacketMonitoring();

            var serviceProvider = serviceCollection.BuildServiceProvider(); 

            var logger = serviceProvider.GetService<ILogger<Program>>();
            var monitor = serviceProvider.GetService<Monitor>();

            monitor.IncomingPlayerEvent += (s, e) =>
            {
                logger.LogInformation($"INCOMING: {e.PlayerState}");
            };
            //monitor.OutgoingPlayerEvent += (s, e) => {
            //    logger.LogInformation($"OUTGOING: {e.PlayerState}");
            //};
            //monitor.IncomingChatMessageEvent += (s, e) => {
            //    logger.LogInformation($"CHAT: {e.Message}");
            //};
            //monitor.IncomingPlayerEnteredWorldEvent += (s, e) => {
            //    logger.LogInformation($"WORLD: {e.PlayerUpdate}");
            //};
            //monitor.IncomingRideOnGivenEvent += (s, e) => {
            //    logger.LogInformation($"RIDEON: {e.RideOn}");
            //};

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

            Console.ReadLine();
          
            // Stop monitoring
            monitor.StopCaptureAsync().Wait();
        }
    }
}
