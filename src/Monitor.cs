using System;
using System.Linq;
using SharpPcap;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;

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
    /// This is a .Net Core port of the Node zwift-packet-monitor project (https://github.com/wiedmann/zwift-packet-monitor).
    ///</summary>
    ///<author>Brad Walker - https://github.com/braddwalker/ZwiftPacketMonitor/</author>
    public class Monitor 
    {
        /// <summary>
        /// The default Zwift UDP data port
        /// </summary>
        private const int ZWIFT_UDP_PORT = 3022;

        private const int ZWIFT_TCP_PORT = 3023;

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
            logger.LogDebug($"Starting packet capture on {networkInterface} UDP:{ZWIFT_UDP_PORT}, TCP:{ZWIFT_TCP_PORT}");

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
            //device.Filter = $"udp port {ZWIFT_UDP_PORT} or tcp port {ZWIFT_TCP_PORT}";
            device.Filter = $"udp port {ZWIFT_UDP_PORT}";

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
                var packet = Packet.ParsePacket(e.Packet.LinkLayerType, e.Packet.Data);
                var tcpPacket = packet.Extract<TcpPacket>();
                var udpPacket = packet.Extract<UdpPacket>();

                var packetBytes = new byte[0];
                var protoBytes = new byte[0];
                var direction = Direction.Unknown;
                
                if (tcpPacket != null) {
                    var ipPacket = (IPPacket)tcpPacket.ParentPacket;
                    int srcPort = tcpPacket.SourcePort;
                    int dstPort = tcpPacket.DestinationPort;

                    packetBytes = tcpPacket.PayloadData;

                    //Incoming packet
                    if (srcPort == ZWIFT_TCP_PORT)
                    {
                        // Always skip the first 2 bytes to get to the protobuf
                       protoBytes = packetBytes.Skip(2).ToArray(); 
                       direction = Direction.Incoming;
                    }
                    // Outgoing packet
                    else if (dstPort == ZWIFT_TCP_PORT)
                    {
                        // Currently no support for outbound TCP packets
                        packetBytes = new byte[0];
                        protoBytes = new byte[0];
                        direction = Direction.Outgoing;
                    }
                }
                else if (udpPacket != null)
                {
                    var ipPacket = (IPPacket)udpPacket.ParentPacket;
                    int srcPort = udpPacket.SourcePort;
                    int dstPort = udpPacket.DestinationPort;

                    packetBytes = udpPacket.PayloadData;

                    //Incoming packet
                    if (srcPort == ZWIFT_UDP_PORT)
                    {
                        protoBytes = packetBytes;
                        direction = Direction.Incoming;
                    }
                    // Outgoing packet
                    else if (dstPort == ZWIFT_UDP_PORT)
                    {
                        // Outgoing packets *may* have some a metadata header that's not part of the protobuf.
                        // This is sort of a magic number at the moment -- not sure if the first byte is coincidentally 0x06, 
                        // or if the header (if there is one) is always 5 bytes long
                        int skip = 5;

                        if (packetBytes[skip] == 0x08) {
                            // NOOP, as the protobuf payload looks like it starts after the initial skip estimate
                        }
                        else if (packetBytes[0] == 0x08) {
                            // protobuf payload starts at the beginning
                            skip = 0;
                        }
                        else {
                            // Use the first byte as an indicator of how far into the payload we need to look
                            // in order to find the beginning of the protobuf
                            skip = packetBytes[0] - 1;
                        }

                        protoBytes = packetBytes.Skip(skip).ToArray();
                        protoBytes = protoBytes.Take(protoBytes.Length - 4).ToArray();
                        direction = Direction.Outgoing;
                    }
                }

                if (protoBytes?.Length > 0)
                {
                    try 
                    {
                        if (direction == Direction.Outgoing)
                        {
                            var packetData = ClientToServer.Parser.ParseFrom(protoBytes);
                            if (packetData.State != null) 
                            {
                                PlayerStateEventArgs args = new PlayerStateEventArgs();
                                args.PlayerState = packetData.State;
                                args.EventDate = DateTime.Now;
                                OnOutgoingPlayerEvent(args);
                            }
                        }
                        else if (direction == Direction.Incoming)
                        {
                            var packetData = ServerToClient.Parser.ParseFrom(protoBytes);

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

                            // Dispatch player updates individually
                            foreach (var pu in packetData.PlayerUpdates)
                            {
                                switch (pu.Tag3)
                                {
                                    case 2:
                                        var payload2 = Payload2.Parser.ParseFrom(pu.Payload.ToByteArray());
                                        Console.WriteLine($"Payload2: {payload2}");
                                        break;
                                    case 3:
                                        var payload3 = Payload3.Parser.ParseFrom(pu.Payload.ToByteArray());
                                        Console.WriteLine($"Payload3: {payload3}");
                                        break;
                                    case 4:
                                        var payload4 = Payload4.Parser.ParseFrom(pu.Payload.ToByteArray());
                                        Console.WriteLine($"Payload4: {payload4}");
                                        break;
                                    case 5:
                                        var payload5 = Payload5.Parser.ParseFrom(pu.Payload.ToByteArray());
                                        Console.WriteLine($"Payload5: {payload5}");
                                        break;
                                    case 105:
                                        var payload105 = Payload105.Parser.ParseFrom(pu.Payload.ToByteArray());
                                        Console.WriteLine($"Payload105: {payload105}");
                                        break;
                                    case 109:
                                        var payload109 = Payload109.Parser.ParseFrom(pu.Payload.ToByteArray());
                                        Console.WriteLine($"Payload109: {payload109}");
                                        break;
                                    case 110:
                                        var payload110 = Payload110.Parser.ParseFrom(pu.Payload.ToByteArray());
                                        Console.WriteLine($"Payload110: {payload110}");
                                        break;
                                    default:
                                        break;
                                }                            
                            }
                        }
                    }
                    catch (Exception ex) {
                        logger.LogError(ex, $"ERROR: PayloadLen: {packetBytes?.Length}, PayloadData: {BitConverter.ToString(packetBytes).Replace("-", "")}\n\r");
                    }
                }
            }
            catch (Exception ee)
            {
                logger.LogError(ee, $"Unable to parse packet");
            }
        }
   }

   public enum Direction 
   {
       Unknown,
       Incoming,
       Outgoing
   }
}