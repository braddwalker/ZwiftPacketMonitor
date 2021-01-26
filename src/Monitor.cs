using System;
using System.Linq;
using SharpPcap;
using SharpPcap.Npcap;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Net;
using Microsoft.Extensions.Logging;
using PacketDotNet;

namespace ZwiftPacketMonitor
{
    ///<summary>
    /// This class implements a TCP and UDP packet monitor for the Zwift cycling simulator. It listens for packets on a specific port
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
        private const int ZWIFT_UDP_PORT = 3022;

        /// <summary>
        /// The default Zwift TCP data port
        /// </summary>
        private const int ZWIFT_TCP_PORT = 3023;

        /// <summary>
        /// Default read timeout for packet capture
        /// </summary>
        private const int READ_TIMEOUT = 1000;

        /// <summary>
        /// This event gets invoked when player update events are received from the central Zwift game engine
        /// </summary>
        public event EventHandler<PlayerStateEventArgs> IncomingPlayerEvent;

        /// <summary>
        /// This event gets invoked when the local player updates get replicated to the central Zwift game engine
        /// </summary>
        public event EventHandler<PlayerStateEventArgs> OutgoingPlayerEvent;

        /// <summary>
        /// This event gets invoked when a remote player enteres the world
        /// </summary>
        public event EventHandler<PlayerEnteredWorldEventArgs> IncomingPlayerEnteredWorldEvent;

        /// <summary>
        /// This event gets invoked when a remote player gives a ride on to another player
        /// </summary>
        public event EventHandler<RideOnGivenEventArgs> IncomingRideOnGivenEvent;

        /// <summary>
        /// This event gets invoked when a remote player sends a chat message
        /// </summary>
        public event EventHandler<ChatMessageEventArgs> IncomingChatMessageEvent;

        private NpcapDevice _device;
        private ILogger<Monitor> _logger;
        private PacketAssembler _packetAssembler;

        /// <summary>
        /// Creates a new instance of the monitor class.
        /// </summary>
        public Monitor(ILogger<Monitor> logger, PacketAssembler packetAssembler) 
        {
            _logger = logger ?? throw new ArgumentException(nameof(logger));

            // Setup the packet assembler and callback
            this._packetAssembler = packetAssembler ?? throw new ArgumentException(nameof(packetAssembler));
            this._packetAssembler.PayloadReady += packet_OnPayloadReady;
        }

        private void OnIncomingChatMessageEvent(ChatMessageEventArgs e)
        {
            EventHandler<ChatMessageEventArgs> handler = IncomingChatMessageEvent;
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

        private void OnIncomingRideOnGivenEvent(RideOnGivenEventArgs e)
        {
            EventHandler<RideOnGivenEventArgs> handler = IncomingRideOnGivenEvent;
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

        private void OnIncomingPlayerEnteredWorldEvent(PlayerEnteredWorldEventArgs e)
        {
            EventHandler<PlayerEnteredWorldEventArgs> handler = IncomingPlayerEnteredWorldEvent;
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
        /// <param name="networkInterface">The name or IP address of the network interface to attach to</param>
        /// <param name="cancellationToken">An optional cancellation token</param>
        /// <returns>A Task representing the running packet capture</returns>
        public async Task StartCaptureAsync(string networkInterface, CancellationToken cancellationToken = default)
        {            
            // This will blow up if caller doesn't have sufficient privs to attach to network devices
            var devices = NpcapDeviceList.Instance;

            // Roll the dice and pull the first interface in the list
            if (string.IsNullOrWhiteSpace(networkInterface))
            {
                _device = devices.FirstOrDefault(d => d.Addresses.Count > 0);
            }
            else
            {
                // See if we can find the desired interface by name
                if (Regex.IsMatch(networkInterface, "^(?:[0-9]{1,3}\\.){3}[0-9]{1,3}$"))
                {
                    _logger.LogDebug($"Searching for device matching {networkInterface}");
                    _device = devices.FirstOrDefault(d => 
                        d.Addresses != null && d.Addresses.Any(a => 
                            a.Addr != null && a.Addr.ipAddress != null && 
                                a.Addr.ipAddress.Equals(IPAddress.Parse(networkInterface))));
                }
                else 
                {
                    _device = devices.Where(x => 
                        x.Name.Equals(networkInterface, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
                }
            }

            if (_device == null)
            {
                throw new ArgumentException($"Interface {networkInterface} not found");
            }

            _logger.LogDebug($"Starting packet capture on {GetInterfaceDisplayName(_device)} UDP:{ZWIFT_UDP_PORT}, TCP: {ZWIFT_TCP_PORT}");

            // Open the device for capturing
            _device.Open(DeviceMode.Normal, READ_TIMEOUT);
            _device.Filter = $"udp port {ZWIFT_UDP_PORT} or tcp port {ZWIFT_TCP_PORT}";
            _device.OnPacketArrival += new PacketArrivalEventHandler(device_OnPacketArrival);

            // Start capture 'INFINTE' number of packets
            await Task.Run(() => { _device.Capture(); }, cancellationToken);
        }

        private string GetInterfaceDisplayName(NpcapDevice device) 
        {
            return (device.Addresses[0]?.Addr?.ipAddress == null ? device.Name : device.Addresses[0].Addr.ipAddress.ToString());
        }

        /// <summary>
        /// Stops any active capture
        /// </summary>
        /// <param name="cancellationToken">An optional cancellation token</param>
        /// <returns>A Task representing the stopped operation</returns>
        public async Task StopCaptureAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Sopping packet capture");

            if (_device == null)
            {
                await Task.CompletedTask;
            }
            else 
            {
                await Task.Run(() => { _device.Close(); }, cancellationToken);
            }
        }

        private void device_OnPacketArrival(object sender, CaptureEventArgs e)
        {
            try 
            {
                var packet = Packet.ParsePacket(e.Packet.LinkLayerType, e.Packet.Data);
                var tcpPacket = packet.Extract<TcpPacket>();
                var udpPacket = packet.Extract<UdpPacket>();

                var protoBytes = new byte[0];
                var direction = Direction.Unknown;
                
                if (tcpPacket != null) 
                {
                    int srcPort = tcpPacket.SourcePort;
                    int dstPort = tcpPacket.DestinationPort;

                    //Only incoming packets are supported
                    if (srcPort == ZWIFT_TCP_PORT)
                    {
                        // TCP packets are often fragmented due to payloads that are larger
                        // than the MTU size, so we need to do some extra work to reassemble
                        // them and reconstruct the protobuf data.
                        _packetAssembler.Assemble(tcpPacket);
                    }
                    else if (dstPort == ZWIFT_TCP_PORT)
                    {
                        // these packets don't contain any payload
                    }
                }
                else if (udpPacket != null)
                {
                    int srcPort = udpPacket.SourcePort;
                    int dstPort = udpPacket.DestinationPort;

                    var packetBytes = udpPacket.PayloadData;

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

                        // Pluck the protobuf data from the packet payload
                        protoBytes = packetBytes.Skip(skip).ToArray();
                        protoBytes = protoBytes.Take(protoBytes.Length - 4).ToArray();
                        direction = Direction.Outgoing;
                    }

                    DeserializeAndDispatch(protoBytes, direction);
                }
            }
            catch (Exception ee)
            {
                _logger.LogError(ee, $"Unable to parse packet");
            }
        }

        private void packet_OnPayloadReady(object sender, PayloadReadyEventArgs e)
        {
            // Only incoming TCP payloads are coming through here
            DeserializeAndDispatch(e.Payload, Direction.Incoming);
        }

        private void DeserializeAndDispatch(byte[] buffer, Direction direction)
        {
            // If we have any data to deserialize at this point, let's continue
            if (buffer?.Length > 0)
            {
                try 
                {
                    // Depending on the direction, we need to use different protobuf parsers
                    if (direction == Direction.Outgoing)
                    {
                        var packetData = ClientToServer.Parser.ParseFrom(buffer);
                        if (packetData.State != null) 
                        {
                            // Dispatch the event
                            OnOutgoingPlayerEvent(new PlayerStateEventArgs()
                            {
                                PlayerState = packetData.State,
                                EventDate = DateTime.Now
                            });
                        }
                    }
                    else if (direction == Direction.Incoming)
                    {
                        var packetData = ServerToClient.Parser.ParseFrom(buffer);

                        // Dispatch each player state individually
                        foreach (var player in packetData.PlayerStates)
                        {
                            if (player != null) 
                            {
                                // Dispatch the event
                                OnIncomingPlayerEvent(new PlayerStateEventArgs()
                                {
                                    PlayerState = player,
                                    EventDate = DateTime.Now
                                });
                            }
                        }

                        // Dispatch player updates individually
                        foreach (var pu in packetData.PlayerUpdates)
                        {
                            switch (pu.Tag3)
                            {                                    
                                case 4:
                                    OnIncomingRideOnGivenEvent(new RideOnGivenEventArgs() 
                                    {
                                        RideOn = Payload4.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                        EventDate = DateTime.Now
                                    });
                                    break;
                                case 5:
                                    OnIncomingChatMessageEvent(new ChatMessageEventArgs()
                                    {
                                        Message = Payload5.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                        EventDate = DateTime.Now
                                    });
                                    break;
                                case 105:
                                    OnIncomingPlayerEnteredWorldEvent(new PlayerEnteredWorldEventArgs()
                                    {
                                        PlayerUpdate = Payload105.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                        EventDate = DateTime.Now
                                    });
                                    break;
                                /*
                                case 2:
                                    var payload2 = Payload2.Parser.ParseFrom(pu.Payload.ToByteArray());
                                    Console.WriteLine($"Payload2: {payload2}");
                                    break;
                                case 3:
                                    var payload3 = Payload3.Parser.ParseFrom(pu.Payload.ToByteArray());
                                    Console.WriteLine($"Payload3: {payload3}");
                                    break;
                                case 109:
                                    var payload109 = Payload109.Parser.ParseFrom(pu.Payload.ToByteArray());
                                    Console.WriteLine($"Payload109: {payload109}");
                                    break;
                                case 110:
                                    var payload110 = Payload110.Parser.ParseFrom(pu.Payload.ToByteArray());
                                    Console.WriteLine($"Payload110: {payload110}");
                                    break;
                                */
                                default:
                                    break;
                            }                            
                        }
                    }
                }
                catch (Exception ex) 
                {
                    _logger.LogError(ex, $"ERROR: Actual: {buffer?.Length}, PayloadData: {BitConverter.ToString(buffer).Replace("-", "").Substring(0, 25)}...\n\r");
                }   
            }
        }
   }

    /// <summary>
    /// This enumeration defines whether a given packet of data
    /// is incoming from the remote server, or outgoing from the local client
    /// </summary>
   public enum Direction 
   {
       // Default value
       Unknown,
       // Incoming from the remote server
       Incoming,
       // Outgoing from the local client
       Outgoing
   }
}