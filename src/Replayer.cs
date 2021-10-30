using System.IO;
using Microsoft.Extensions.Logging;
using SharpPcap;
using SharpPcap.LibPcap;

namespace ZwiftPacketMonitor
{
    /// <summary>
    /// Reads Zwift messages from a PCAP file instead of a network device
    /// </summary>
    public class Replayer : Monitor
    {
        public Replayer(
            ILogger<Monitor> logger, 
            PacketAssembler packetAssembler,
            PacketAssembler companionPacketAssemblerPcToApp, 
            PacketAssembler companionPacketAssemblerAppToPc,
            CompanionPacketDecoder companionPacketDecoder) 
            : base(logger, packetAssembler, companionPacketAssemblerPcToApp, companionPacketAssemblerAppToPc, companionPacketDecoder)
        {
        }

        public void FromCapture(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Capture file not found", path);
            }

            using var device = new CaptureFileReaderDevice(path);

            device.Open();
            device.OnPacketArrival += device_OnPacketArrival;
            device.Capture();
        }
    }
}