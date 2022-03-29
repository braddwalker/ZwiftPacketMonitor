using System;
using System.Linq;
using SharpPcap;
using SharpPcap.LibPcap;
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
        public event EventHandler<SegmentResultEventArgs> IncomingSegmentResultEvent;

        /// <summary>
        /// This event gets invoked when a remote player gives a ride on to another player
        /// </summary>
        public event EventHandler<RideOnGivenEventArgs> IncomingRideOnGivenEvent;

        /// <summary>
        /// This event gets invoked when a remote player sends a chat message
        /// </summary>
        public event EventHandler<SocialPlayerActionEventArgs> IncomingSocialPlayerActionEvent;

        /// <summary>
        /// This event gets invoked when a remote player's world time needs to be synced
        /// </summary>
        public event EventHandler<PlayerLeftWorldEventArgs> IncomingPlayerLeftWorldEvent;

        /// <summary>
        /// This event gets invoked when a meetup gets scheduled or updated
        /// </summary>
        public event EventHandler<EventProtobufEventArgs> IncomingEventProtobufEvent;

        /// <summary>
        /// This event gets invoked when the player joined event
        /// </summary>
        public event EventHandler<PlayerJoinedEventArgs> IncomingPlayerJoinedEvent;

        /// <summary>
        /// This event gets invoked when the player left event
        /// </summary>
        public event EventHandler<PlayerLeftEventArgs> IncomingPlayerLeftEvent;

        /// <summary>
        /// This event gets invoked when the player invited to event
        /// </summary>
        public event EventHandler<EventProtobufEventArgs> IncomingEventInviteEvent;

        /// <summary>
        /// This event gets invoked when notable moment occurred
        /// </summary>
        public event EventHandler<NotableEventArgs> IncomingNotableEvent;

        /// <summary>
        /// This event gets invoked when group event occurred
        /// </summary>
        public event EventHandler<GroupEventArgs> IncomingGroupEvent;

        /// <summary>
        /// This event gets invoked when bike action occurred
        /// </summary>
        public event EventHandler<BikeActionEventArgs> IncomingBikeActionEvent;

        /// <summary>
        /// This event gets invoked when no-payload WorldAttribute received
        /// </summary>
        public event EventHandler<NoPayloadWaEventArgs> IncomingNoPayloadWaEvent;

        /// <summary>
        /// This event gets invoked when string message WorldAttribute received
        /// </summary>
        public event EventHandler<MessageWaEventArgs> IncomingMessageWaEvent;

        /// <summary>
        /// This event gets invoked when time-sync WorldAttribute received
        /// </summary>
        public event EventHandler<FloatTimeWaEventArgs> IncomingFloatTimeWaEvent;

        /// <summary>
        /// This event gets invoked when 'flag' WorldAttribute received
        /// </summary>
        public event EventHandler<FlagWaEventArgs> IncomingFlagWaEvent;

        /// <summary>
        /// This event gets invoked during events and reports rider positions
        /// </summary>
        public event EventHandler<EventSubgroupPlacementsEventArgs> IncomingEventSubgroupPlacementsEvent;

        /// <summary>
        /// A flag that indicates whether packet capture is currently running or not
        /// </summary>
        /// <value>true if running</value>
        public bool IsRunning {get; private set;}

        private LibPcapLiveDevice _device;
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
            this._packetAssembler.PayloadReady += (s, e) =>
            {
                // Only incoming TCP payloads are coming through here
                DeserializeAndDispatch(e.Payload, Direction.Incoming);
            };
        }

        private void OnIncomingEventSubgroupPlacementsEvent(EventSubgroupPlacementsEventArgs e)
        {
            var handler = IncomingEventSubgroupPlacementsEvent;
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

        private void OnIncomingMeetupEvent(EventProtobufEventArgs e)
        {
            var handler = IncomingEventProtobufEvent;
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

        private void OnIncomingPlayerLeftEvent(PlayerLeftEventArgs e)
        {
            var handler = IncomingPlayerLeftEvent;
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

        private void OnIncomingPlayerJoinedEvent(PlayerJoinedEventArgs e)
        {
            var handler = IncomingPlayerJoinedEvent;
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

        private void OnIncomingEventInviteEvent(EventProtobufEventArgs e)
        {
            var handler = IncomingEventInviteEvent;
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

        private void OnIncomingNotableEvent(NotableEventArgs e)
        {
            var handler = IncomingNotableEvent;
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

        private void OnIncomingGroupEvent(GroupEventArgs e)
        {
            var handler = IncomingGroupEvent;
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

        private void OnIncomingBikeActionEvent(BikeActionEventArgs e)
        {
            var handler = IncomingBikeActionEvent;
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

        private void OnIncomingNoPayloadWaEvent(NoPayloadWaEventArgs e)
        {
            var handler = IncomingNoPayloadWaEvent;
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

        private void OnIncomingMessageWaEvent(MessageWaEventArgs e)
        {
            var handler = IncomingMessageWaEvent;
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

        private void OnIncomingFloatTimeWaEvent(FloatTimeWaEventArgs e)
        {
            var handler = IncomingFloatTimeWaEvent;
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

        private void OnIncomingFlagWaEvent(FlagWaEventArgs e)
        {
            var handler = IncomingFlagWaEvent;
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

        private void OnIncomingSocialPlayerActionEvent(SocialPlayerActionEventArgs e)
        {
            var handler = IncomingSocialPlayerActionEvent;
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
            var handler = IncomingRideOnGivenEvent;
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

        private void OnIncomingSegmentResultEvent(SegmentResultEventArgs e)
        {
            var handler = IncomingSegmentResultEvent;
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
            var handler = IncomingPlayerEvent;
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
            var handler = OutgoingPlayerEvent;
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

        private void OnIncomingPlayerLeftWorldEvent(PlayerLeftWorldEventArgs e)
        {
            var handler = IncomingPlayerLeftWorldEvent;
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
            var devices = LibPcapLiveDeviceList.Instance;

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
                    _device = devices.FirstOrDefault(x => 
                        x.Name.Equals(networkInterface, StringComparison.InvariantCultureIgnoreCase));

                    if (_device == null) // still unresolved, search by friendly name (ie. ipconfig /all)
                    {
                        _device = devices.FirstOrDefault(x => 
                            x.Interface.FriendlyName != null && x.Interface.FriendlyName.Equals(networkInterface, StringComparison.InvariantCultureIgnoreCase));
                    }
                }
            }

            if (_device == null)
            {
                throw new ArgumentException($"Interface {networkInterface} not found");
            }

            _logger.LogDebug($"Starting packet capture on {GetInterfaceDisplayName(_device)} UDP:{ZWIFT_UDP_PORT}, TCP: {ZWIFT_TCP_PORT}");

            // Open the device for capturing
            _device.Open(mode: DeviceModes.Promiscuous | DeviceModes.DataTransferUdp | DeviceModes.NoCaptureLocal, read_timeout: READ_TIMEOUT);
            _device.Filter = $"udp port {ZWIFT_UDP_PORT} or tcp port {ZWIFT_TCP_PORT}";
            _device.OnPacketArrival += new PacketArrivalEventHandler(device_OnPacketArrival);

            IsRunning = true;

            // Start capture 'INFINTE' number of packets
            await Task.Run(() => { _device.Capture(); }, cancellationToken);
        }

        /// <summary>
        /// Stops any active capture
        /// </summary>
        /// <param name="cancellationToken">An optional cancellation token</param>
        /// <returns>A Task representing the stopped operation</returns>
        public async Task StopCaptureAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Stopping packet capture");
            IsRunning = false;

            if (_device == null)
            {
                await Task.CompletedTask;
            }
            else
            {
                await Task.Run(() => { 
                    _device.StopCapture();
                    _device = null;
                }, cancellationToken);
            }
        }

        private string GetInterfaceDisplayName(LibPcapLiveDevice device) 
        {
            return (device.Addresses[0]?.Addr?.ipAddress == null ? device.Name : device.Addresses[0].Addr.ipAddress.ToString());
        }

        private void device_OnPacketArrival(object sender, PacketCapture e)
        {
            try 
            {
                var packet = Packet.ParsePacket(e.Device.LinkType, e.Data.ToArray());
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
                        int skip = 0; //regular frame format with no header
                        if (packetBytes[0] == 0xDF)
                        {
                            skip = 1; // case 1. 0xDF (header is 'don't forward byte')
                        } else if (packetBytes[0] == 0x06) {
                            skip = 5; // case 2. 0x06 & <int32:PlayerId> = 5 bytes to ignore (header 'voronoiOrDieByte')
                        } else {
                            // Use the first byte as an indicator of how far into the payload we need to look
                            // in order to find the beginning of the protobuf
                            // ??? ursoft: did not see this case in decompiled code
                            skip = packetBytes[0] - 1;
                        }

                        // Pluck the protobuf data from the packet payload
                        protoBytes = packetBytes.Skip(skip).ToArray();
                        protoBytes = protoBytes.Take(protoBytes.Length - 4).ToArray(); // payload hash: 4 bytes, ignored
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
                                });
                            }
                        }

                        if (packetData.EvSubgroupPs != null)
                        {
                            // Dispatch the event
                            OnIncomingEventSubgroupPlacementsEvent(new EventSubgroupPlacementsEventArgs()
                            {
                                EvSubgroupPs = packetData.EvSubgroupPs,
                            });
                        }

                        // Dispatch player updates individually
                        foreach (var pu in packetData.PlayerUpdates)
                        {
                            try
                            {
                                switch (pu.WaType)
                                {
                                    case WA_TYPE.WatRideOn:
                                        OnIncomingRideOnGivenEvent(new RideOnGivenEventArgs()
                                        {
                                            RideOn = RideOn.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                            WorldAttribute = pu,
                                        });
                                        break;
                                    case WA_TYPE.WatSpa:
                                        OnIncomingSocialPlayerActionEvent(new SocialPlayerActionEventArgs()
                                        {
                                            SocialPlayerAction = SocialPlayerAction.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                            WorldAttribute = pu,
                                        });
                                        break;
                                    case WA_TYPE.WatSr:
                                        OnIncomingSegmentResultEvent(new SegmentResultEventArgs()
                                        {
                                            SegmentResult = SegmentResult.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                            WorldAttribute = pu,
                                        });
                                        break;
                                    case WA_TYPE.WatRelogin:
                                    case WA_TYPE.WatLeave:
                                        OnIncomingPlayerLeftWorldEvent(new PlayerLeftWorldEventArgs()
                                        {
                                            PlayerLeftWorld = PlayerLeftWorld.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                            WaType = pu.WaType,
                                            WorldAttribute = pu,
                                        });
                                        break;
                                    case WA_TYPE.WatEvent:
                                        OnIncomingMeetupEvent(new EventProtobufEventArgs()
                                        {
                                            EventProtobuf = EventProtobuf.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                            WorldAttribute = pu,
                                        });
                                        break;
                                    case WA_TYPE.WatJoinE:
                                        OnIncomingPlayerJoinedEvent(new PlayerJoinedEventArgs()
                                        {
                                            PlayerJoinedEvent = PlayerJoinedEvent.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                            WorldAttribute = pu,
                                        });
                                        break;
                                    case WA_TYPE.WatLeftE:
                                        OnIncomingPlayerLeftEvent(new PlayerLeftEventArgs()
                                        {
                                            PlayerLeftEvent = PlayerLeftEvent.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                            WorldAttribute = pu,
                                        });
                                        break;
                                    case WA_TYPE.WatInvW:
                                        OnIncomingEventInviteEvent(new EventProtobufEventArgs()
                                        {
                                            EventProtobuf = EventProtobuf.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                            WorldAttribute = pu,
                                        });
                                        break;
                                    case WA_TYPE.WatGrpM:
                                    case WA_TYPE.WatPriM:
                                        OnIncomingMessageWaEvent(new MessageWaEventArgs()
                                        {
                                            //Message = StringProto.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                            WaType = pu.WaType,
                                            WorldAttribute = pu,
                                        }); //did not checked yet
                                        break;
                                    case WA_TYPE.WatWtime: //check: 4-byte world time, int or float?
                                    case WA_TYPE.WatRtime: //check: float road time
                                        OnIncomingFloatTimeWaEvent(new FloatTimeWaEventArgs()
                                        {
                                            FloatTime = BitConverter.ToSingle(pu.Payload.ToByteArray()),  //did not checked yet
                                            WaType = pu.WaType,
                                            WorldAttribute = pu,
                                        });
                                        break;
                                    case WA_TYPE.WatFlag:
                                        OnIncomingFlagWaEvent(new FlagWaEventArgs()
                                        {
                                            FlagWaInfo = new FlagWaInfo(pu.Payload.ToByteArray()),
                                            WorldAttribute = pu,
                                        });
                                        break;
/*todo:
                                    case WA_TYPE.WatRla:
                                    case WA_TYPE.WatLate:
                                    case WA_TYPE.WatRh:
                                    case WA_TYPE.WatStats:
                                    case WA_TYPE.WatFence:
                                    case WA_TYPE.WatBnGe:
                                    case WA_TYPE.WatPpi:*/
                                    case WA_TYPE.WatBAct:
                                        if (pu.Payload.Length == 16)
                                        {
                                            var bytes = pu.Payload.ToByteArray();
                                            OnIncomingBikeActionEvent(new BikeActionEventArgs()
                                            {
                                                BaHexStr = BitConverter.ToString(bytes),
                                                PlayerId = BitConverter.ToInt64(bytes, 0), //did not checked yet
                                                BikeAction = (BikeAction)BitConverter.ToInt32(bytes, 8), //did not checked yet
                                            });
                                            return;
                                        }
                                        _logger.LogWarning($"Unknown WatBAct contents: {pu}, {BitConverter.ToString(pu.Payload.ToByteArray()).Replace("-", "")}");
                                        break;
                                    case WA_TYPE.WatGe:
                                        if (pu.Payload.Length == 40)
                                        {
                                            var bytes = pu.Payload.ToByteArray();
                                            if (bytes[0] == 1 && bytes[1] == 0 && /* version */
                                                bytes[2] == 36 && bytes[3] == 0 /* length */ )
                                            {
                                                OnIncomingGroupEvent(new GroupEventArgs()
                                                {
                                                    GeHexStr = BitConverter.ToString(bytes),
                                                    PlayerId = BitConverter.ToInt64(bytes, 8), //did not checked yet
                                                    GeKindStr = (bytes[25] != 0) ? "UserSignedup" : "UserRegistered", //did not checked yet
                                                });
                                                return;
                                            }
                                        }
                                        _logger.LogWarning($"Unknown WatGe contents: {pu}, {BitConverter.ToString(pu.Payload.ToByteArray()).Replace("-", "")}");
                                        break;
                                    case WA_TYPE.WatRqProf:
                                    case WA_TYPE.WatKicked:
                                    case WA_TYPE.WatNone:
                                        OnIncomingNoPayloadWaEvent(new NoPayloadWaEventArgs()
                                        {
                                            WaType = pu.WaType,
                                        }); //did not checked yet
                                        break;
                                    case WA_TYPE.WatNm:
                                        if (pu.Payload.Length == 48)
                                        {
                                            var bytes = pu.Payload.ToByteArray();
                                            if (bytes[0] == 1 && bytes[1] == 0 && /* version */
                                                bytes[2] == 0x2c && bytes[3] == 0 /* length */ )
                                            {
                                                OnIncomingNotableEvent(new NotableEventArgs()
                                                {
                                                    NotableHexStr = BitConverter.ToString(bytes),
                                                    PlayerId = BitConverter.ToInt64(bytes, 8), //did not checked yet
                                                });
                                                return;
                                            }
                                        }
                                        _logger.LogWarning($"Unknown WatNm contents: {pu}, {BitConverter.ToString(pu.Payload.ToByteArray()).Replace("-", "")}");
                                        break;
                                    case WA_TYPE.WatUnk0:
                                    case WA_TYPE.WatUnk1:
                                    default:
                                        _logger.LogWarning($"Unknown tag {pu.WaType}: {pu}, {BitConverter.ToString(pu.Payload.ToByteArray()).Replace("-", "")}");
                                        break;
                                }
                            }
                            catch (Exception e)
                            {
                                _logger.LogError(e, $"ERROR packetData.PlayerUpdates: Actual: {pu.Payload.Length}, PayloadData: {BitConverter.ToString(pu.Payload.ToByteArray()).Replace("-", "")}\n\r");
                            }
                        }
                    }
                }
                catch (Exception ex) 
                {
                    _logger.LogError(ex, $"ERROR: Actual: {buffer?.Length}, PayloadData: {BitConverter.ToString(buffer).Replace("-", "")}\n\r");
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