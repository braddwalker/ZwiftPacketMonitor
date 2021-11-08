using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ZwiftPacketMonitor.Replay
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("ZwiftPacketMonitor capture replay");

            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: ZwiftPacketMonitor.Replay.exe <path to capture file>");
                Environment.Exit(1);
            }

            var path = args[0];

            if (!File.Exists(path))
            {
                Console.Error.WriteLine("Capture file not found");
                Environment.Exit(1);
            }
            
            var serviceProvider = DependencyRegistration.Register();

            var logger = serviceProvider.GetService<ILogger<Program>>();
            var monitor = serviceProvider.GetRequiredService<Monitor>();
            
            // Register events for Zwift Companion messages
            var decoder = serviceProvider.GetRequiredService<CompanionPacketDecoder>();
            RegisterZwiftCompanionEvents(decoder, logger);

            // Register events for Zwift Desktop app messages
            RegisterZwiftDesktopEvents(monitor, logger);

            // Replay the packet capture
            monitor.StartCaptureAsync(path).GetAwaiter().GetResult();
        }

        private static void RegisterZwiftCompanionEvents(CompanionPacketDecoder decoder, ILogger<Program>? logger)
        {
            decoder.CommandAvailable += (_, eventArgs) =>
            {
                logger.LogInformation("Command {type} is now available", eventArgs.CommandType);
            };

            decoder.CommandSent += (_, eventArgs) =>
            {
                logger.LogInformation("Sent a {type} command", eventArgs.CommandType);
            };
        }

        private static void RegisterZwiftDesktopEvents(Monitor monitor, ILogger<Program> logger)
        {
            monitor.IncomingPlayerEvent += (s, e) => { logger.LogInformation($"INCOMING: {e.PlayerState}"); };
            monitor.OutgoingPlayerEvent += (s, e) => { logger.LogInformation($"OUTGOING: {e.PlayerState}"); };
            monitor.IncomingChatMessageEvent += (s, e) => { logger.LogInformation($"CHAT: {e.Message}"); };
            monitor.IncomingPlayerEnteredWorldEvent += (s, e) => { logger.LogInformation($"WORLD: {e.PlayerUpdate}"); };
            monitor.IncomingRideOnGivenEvent += (s, e) => { logger.LogInformation($"RIDEON: {e.RideOn}"); };
        }
    }
}
