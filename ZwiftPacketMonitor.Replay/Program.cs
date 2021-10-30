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
            var replay = serviceProvider.GetService<Replayer>();
            var messageDiagnostics = serviceProvider
                .GetRequiredService<MessageDiagnostics>();
            messageDiagnostics
                .OutputTo(
                    Path.Combine(
                        Path.GetDirectoryName(path), 
                        Path.GetFileNameWithoutExtension(path)));

            var decoder = serviceProvider.GetRequiredService<CompanionPacketDecoder>();

            decoder.CommandAvailable += (_, eventArgs) =>
            {
                //logger.LogInformation("Command {type} is now available", eventArgs.CommandType);
            };

            decoder.CommandSent += (_, eventArgs) =>
            {
                //logger.LogInformation("Sent a {type} command", eventArgs.CommandType);
            };

            replay.FromCapture(path);

            logger.LogInformation("Messages captured:\n{summary}", messageDiagnostics.GetSummary());
        }
    }
}
