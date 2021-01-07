using System;
using System.Linq;
using SharpPcap;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ZwiftPacketMonitor
{
    ///<summary>
    /// This class implements a UDP packet monitor for the Zwift cycling simulator. It listens for packets on a specific port
    /// of a local network adapter, and when found, deserializes the payload and dispatches events that can be consumed by the
    /// caller.
    /// 
    /// NOTE: Because this utilizes a network packet capture to intercept the UDP packets, your system may require this code to
    /// run using elevated privileges.
    /// 
    /// This is a .NET port of the Node zwift-packet-monitor project (https://github.com/jeroni7100/zwift-packet-monitor).
    ///</summary>
    ///<author>Brad Walker - https://github.com/braddwalker/ZwiftPacketMonitor/</author>
    public class Monitor 
    {
        /// <summary>
        /// The default Zwift UDP data port
        /// </summary>
        private const int ZWIFT_PORT = 3022;

        /// <summary>
        /// Default read timeout for packet capture
        /// </summary>
        private const int READ_TIMEOUT = 1000;

        private ICaptureDevice device;
        private ILogger<Monitor> logger;

        /// <summary>
        /// This event gets invoked when player update events are received from the central Zwift game engine
        /// </summary>
        public event EventHandler<PlayerStateEventArgs> IncomingPlayerEvent;

        /// <summary>
        /// This event gets invoked when the local player updates get replicated to the central Zwift game engine
        /// </summary>
        public event EventHandler<PlayerStateEventArgs> OutgoingPlayerEvent;

        /// <summary>
        /// Creates a new instance of the monitor class.
        /// </summary>
        public Monitor(ILogger<Monitor> logger) {
            this.logger = logger;
        }

        private void OnIncomingPlayerEvent(PlayerStateEventArgs e)
        {
            EventHandler<PlayerStateEventArgs> handler = IncomingPlayerEvent;
            if (handler != null)
            {
                try {
                    handler(this, e);
                }
                catch {
                    // Don't let downstream exceptions bubble up
                }
            }
        }

        private void OnOutgoingPlayerEvent(PlayerStateEventArgs e)
        {
            EventHandler<PlayerStateEventArgs> handler = OutgoingPlayerEvent;
            if (handler != null)
            {
                try {
                    handler(this, e);
                }
                catch {
                    // Don't let downstream exceptions bubble up
                }
            }
        }

        /// <summary>
        /// Starts the network monitor and begins dispatching events
        /// </summary>
        /// <param name="networkInterface">The name of the network interface to attach to</param>
        /// <param name="cancellationToken">An optional cancellation token</param>
        /// <returns>A Task representing the running packet capture</returns>
        public async Task StartCaptureAsync(string networkInterface, CancellationToken cancellationToken = default)
        {            
            logger.LogDebug($"Starting UDP packet capture on {networkInterface}:{ZWIFT_PORT}");

            // This will blow up if caller doesn't have sufficient privs to attach to network devices
            var devices = CaptureDeviceList.Instance;

            // See if we can find the desired interface by name
            device = devices.Where(x => x.Name.Equals(networkInterface, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();

            if (device == null)
            {
                throw new ArgumentException($"Interface {networkInterface} not found");
            }

            // Register our handler function to the 'packet arrival' event
            device.OnPacketArrival += new PacketArrivalEventHandler(device_OnPacketArrival);

            // Open the device for capturing
            device.Open(DeviceMode.Normal, READ_TIMEOUT);
            device.Filter = $"udp port {ZWIFT_PORT}";

            // Start capture 'INFINTE' number of packets
            await Task.Run(() => { device.Capture(); }, cancellationToken);
        }

        /// <summary>
        /// Stops any active capture
        /// </summary>
        /// <param name="cancellationToken">An optional cancellation token</param>
        /// <returns>A Task representing the stopped operation</returns>
        public async Task StopCaptureAsync(CancellationToken cancellationToken = default)
        {
            logger.LogDebug("Sopping packet capture");

            if (device == null)
            {
                await Task.CompletedTask;
            }
            else {
                await Task.Run(() => { device.Close(); }, cancellationToken);
            }
        }

        private void device_OnPacketArrival(object sender, CaptureEventArgs e)
        {
            try 
            {
                var packet = PacketDotNet.Packet.ParsePacket(e.Packet.LinkLayerType, e.Packet.Data);
                var udpPacket = packet.Extract<PacketDotNet.UdpPacket>();
                if (udpPacket != null)
                {
                    var ipPacket = (PacketDotNet.IPPacket)udpPacket.ParentPacket;

                    System.Net.IPAddress srcIp = ipPacket.SourceAddress;
                    System.Net.IPAddress dstIp = ipPacket.DestinationAddress;
                    int srcPort = udpPacket.SourcePort;
                    int dstPort = udpPacket.DestinationPort;

                    try 
                    {
                        //Incoming packet
                        if (srcPort == ZWIFT_PORT)
                        {                            
                            var packetData = ServerToClient.Parser.ParseFrom(udpPacket.PayloadData);

                            // Dispatch each player state individually
                            foreach (var player in packetData.PlayerStates)
                            {
                                if (player != null) 
                                {
                                    PlayerStateEventArgs args = new PlayerStateEventArgs();
                                    args.PlayerState = player;
                                    args.EventDate = DateTime.Now;
                                    OnIncomingPlayerEvent(args);
                                }
                            }
                        }
                        // Outgoing packet
                        else if (dstPort == ZWIFT_PORT) 
                        {
                            // Outgoing packets have some metadeta at the head of the payload.
                            // First byte tells you how far into the array you need to skip in
                            // order to get to the serialized proto document. 
                            int skip = udpPacket.PayloadData[0] - 1;
                            var packetBytes = udpPacket.PayloadData.Skip(skip).ToArray();
                            packetBytes = packetBytes.Take(packetBytes.Length - 4).ToArray();

                            var packetData = ClientToServer.Parser.ParseFrom(packetBytes);
                            if (packetData.State != null) 
                            {
                                PlayerStateEventArgs args = new PlayerStateEventArgs();
                                args.PlayerState = packetData.State;
                                args.EventDate = DateTime.Now;
                                OnOutgoingPlayerEvent(args);
                            }
                        }
                    }
                    catch (Exception ex) {
                        logger.LogError(ex, $"ERROR: PayloadLen: {udpPacket?.PayloadData?.Length}, SrcPort: {srcPort}, DestPort: {dstPort}");
                    }
                }
            }
            catch (Exception ee)
            {
                logger.LogError(ee, $"Unable to parse packet");
            }
        }
   }
}