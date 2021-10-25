using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        /// The default Zwift TCP data port used by Zwift Companion
        /// </summary>
        private const int ZWIFT_COMPANION_TCP_PORT = 21587;

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

        /// <summary>
        /// This event gets invoked when a remote player's world time needs to be synced
        /// </summary>
        public event EventHandler<PlayerTimeSyncEventArgs> IncomingPlayerTimeSyncEvent;

        /// <summary>
        /// This event gets invoked when a meetup gets scheduled or updated
        /// </summary>
        public event EventHandler<MeetupEventArgs> IncomingMeetupEvent;

        /// <summary>
        /// This event gets invoked during events and reports rider positions
        /// </summary>
        public event EventHandler<EventPositionsEventArgs> IncomingEventPositionsEvent;

        /// <summary>
        /// A flag that indicates whether packet capture is currently running or not
        /// </summary>
        /// <value>true if running</value>
        public bool IsRunning {get; private set;}

        private LibPcapLiveDevice _device;
        private readonly ILogger<Monitor> _logger;
        private readonly PacketAssembler _packetAssembler;
        private readonly PacketAssembler _companionPacketAssemblerPcToApp;
        private readonly PacketAssembler _companionPacketAssemblerAppToPc;
        private int _tcpPacketCounter = 0;
        private int _udpPacketCounter = 0;
        private readonly Dictionary<string, int> _messageTypeCounters = new Dictionary<string, int>();

        /// <summary>
        /// Creates a new instance of the monitor class.
        /// </summary>
        public Monitor(ILogger<Monitor> logger, PacketAssembler packetAssembler, PacketAssembler companionPacketAssemblerPcToApp) 
        {
            _logger = logger ?? throw new ArgumentException(nameof(logger));

            // Setup the packet assembler and callback
            this._packetAssembler = packetAssembler ?? throw new ArgumentException(nameof(packetAssembler));
            this._packetAssembler.PayloadReady += (s, e) =>
            {
                // Only incoming TCP payloads are coming through here
                DeserializeAndDispatch(e.Payload, Direction.Incoming, isTcp: true);
            };

            _companionPacketAssemblerPcToApp = companionPacketAssemblerPcToApp;
            _companionPacketAssemblerPcToApp.PayloadReady += (s, e) =>
            {
                // Only incoming TCP payloads are coming through here
                DeserializeAndDispatchCompanion(e.Payload, Direction.Incoming);
            };

            _companionPacketAssemblerAppToPc = companionPacketAssemblerPcToApp;
            _companionPacketAssemblerAppToPc.PayloadReady += (s, e) =>
            {
                // Only incoming TCP payloads are coming through here
                DeserializeAndDispatchCompanion(e.Payload, Direction.Outgoing);
            };

        }

        private void OnIncomingEventPositionsEvent(EventPositionsEventArgs e)
        {
            var handler = IncomingEventPositionsEvent;
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

        private void OnIncomingMeetupEvent(MeetupEventArgs e)
        {
            var handler = IncomingMeetupEvent;
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

        private void OnIncomingChatMessageEvent(ChatMessageEventArgs e)
        {
            var handler = IncomingChatMessageEvent;
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

        private void OnIncomingPlayerEnteredWorldEvent(PlayerEnteredWorldEventArgs e)
        {
            var handler = IncomingPlayerEnteredWorldEvent;
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

        private void OnIncomingPlayerTimeSyncEvent(PlayerTimeSyncEventArgs e)
        {
            var handler = IncomingPlayerTimeSyncEvent;
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

        protected void device_OnPacketArrival(object sender, PacketCapture e)
        {
            try 
            {
                var packet = Packet.ParsePacket(e.Device.LinkType, e.Data.ToArray());
                var tcpPacket = packet.Extract<TcpPacket>();
                var udpPacket = packet.Extract<UdpPacket>();

                var protoBytes = Array.Empty<byte>();
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
                    else if (srcPort == ZWIFT_COMPANION_TCP_PORT)
                    {
                        _companionPacketAssemblerAppToPc.Assemble(tcpPacket);
                    }
                    else if (dstPort == ZWIFT_COMPANION_TCP_PORT)
                    {
                        _companionPacketAssemblerPcToApp.Assemble(tcpPacket);
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

        private void DeserializeAndDispatchCompanion(byte[] buffer, Direction direction)
        {
            if (direction == Direction.Incoming)
            {
                var packetData = ZwiftCompanionMessage.Parser.ParseFrom(buffer);

                if (packetData.CompanionMessage2 != null)
                {
                    var messageType = packetData.CompanionMessage2.Type;

                    StoreMessageType(messageType, buffer, direction);

                    _logger.LogInformation("Found type {messageType} message", messageType);
                }
                //else if (packetData.Tag10 == 0) // For this message type this always seems to be zero
                //{
                //    var bigPacket = ZwiftCompanionMessageRiderPosition.Parser.ParseFrom(buffer);
                //    var clockTime = DateTimeOffset.FromUnixTimeSeconds((long)bigPacket.ClockTime);
                //    //_logger.LogInformation($"[{_tcpPacketCounter:00000}] {packetData.Tag10:000}: ZwiftCompanionMessageRiderPosition: {clockTime:yyyy-MM-dd HH:mm:ssZ} {bigPacket.Tag5} {bigPacket.Tag6:0.000000} {bigPacket.Tag7:0.000000} {bigPacket.Tag8:0.000000}");
                //}
                else
                {
                    StoreMessageType(999, buffer, direction);
                }
            }
            else if (direction == Direction.Outgoing)
            {
                var message = ZwiftToCompanionMessage.Parser.ParseFrom(buffer);

                if (message.Tag1 == 0)
                {
                    var typeZero = ZwiftToCompanionMessageValueZero.Parser.ParseFrom(buffer);

                    _logger.LogInformation("Received type zero message");

                    StoreMessageType(0, buffer, direction);
                }
                else if (message.Tag1 == 1)
                {
                    var typeOne = ZwiftToCompanionMessageValueOne.Parser.ParseFrom(buffer);

                    _logger.LogInformation("Received type one message");

                    StoreMessageType(1, buffer, direction);
                }
                else if (message.Tag1 == 2)
                {
                    var typeTwo = ZwiftToCompanionMessageValueTwo.Parser.ParseFrom(buffer);

                    _logger.LogInformation("Received type two message");

                    StoreMessageType(2, buffer, direction);
                }
                else
                {
                    StoreMessageType(999, buffer, direction);
                }
            }

            _tcpPacketCounter++;
        }

        private void StoreMessageType(uint messageType, byte[] buffer, Direction direction)
        {
            var basePath = $"c:\\git\\temp\\zwift\\companion-04-tcp-apptozwift";

            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            var type = $"{direction.ToString().ToLower()}-{messageType}";

            if (!_messageTypeCounters.ContainsKey(type))
            {
                _messageTypeCounters.Add(type, 0);
            }

            if (_messageTypeCounters[type] < 10)
            {
                File.WriteAllBytes(
                    $"{basePath}\\{_tcpPacketCounter:00000}-{direction.ToString().ToLower()}-{messageType:000}.bin",
                    buffer);

                _messageTypeCounters[type]++;
            }
        }

        private void DeserializeAndDispatch(byte[] buffer, Direction direction, bool isTcp = false)
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

                        if (packetData.EventPositions != null)
                        {
                            // Dispatch the event
                            OnIncomingEventPositionsEvent(new EventPositionsEventArgs()
                            {
                                EventPositions = packetData.EventPositions,
                            });
                        }

                        // Dispatch player updates individually
                        foreach (var pu in packetData.PlayerUpdates)
                        {
                            try
                            {
                                switch (pu.Tag3)
                                {                                    
                                    case 4:
                                        OnIncomingRideOnGivenEvent(new RideOnGivenEventArgs() 
                                        {
                                            RideOn = RideOn.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                        });
                                        break;
                                    case 5:
                                        OnIncomingChatMessageEvent(new ChatMessageEventArgs()
                                        {
                                            Message = Chat.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                        });
                                        break;
                                    case 105:
                                        OnIncomingPlayerEnteredWorldEvent(new PlayerEnteredWorldEventArgs()
                                        {
                                            PlayerUpdate = Payload105.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                        });
                                        break;
                                    case 3:
                                        OnIncomingPlayerTimeSyncEvent(new PlayerTimeSyncEventArgs()
                                        {
                                            TimeSync = TimeSync.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                        });
                                        break;
                                    case 6:
                                        // meetup create/update? 6 has the same payload as 10
                                    case 10:
                                        // join meetup?
                                        OnIncomingMeetupEvent(new MeetupEventArgs()
                                        {
                                            Meetup = Meetup.Parser.ParseFrom(pu.Payload.ToByteArray()),
                                        });
                                        break;
                                    case 102:
                                    case 109:
                                    case 110:
                                    case 106:
                                        //File.WriteAllBytes(@"c:\git\temp\zwift\pl106.bin", buffer);
                                        //File.WriteAllBytes(@"c:\git\temp\zwift\pl106-payload.bin", pu.Payload.ToByteArray());
                                        //_logger.LogWarning($"Unknown tag {pu.Tag3}: {pu}, {BitConverter.ToString(pu.Payload.ToByteArray()).Replace("-", "")}");
                                        //var data = Payload106.Parser.ParseFrom(pu.Payload.ToByteArray());
                                        //break;
                                    case 116:
                                        //File.WriteAllBytes(@"c:\git\temp\zwift\pl116.bin", buffer);
                                        //File.WriteAllBytes(@"c:\git\temp\zwift\pl116-payload.bin", pu.Payload.ToByteArray());
                                        //_logger.LogWarning($"116 payload: {pu.Tag3}: {pu}, {BitConverter.ToString(pu.Payload.ToByteArray()).Replace("-", "")}");
                                        //OnIncomingMessage116Event(new Message116EventArgs
                                        //{
                                        //    Message = Payload116.Parser.ParseFrom(pu.Payload.ToByteArray())
                                        //});
                                        break;
                                    default:
                                        _logger.LogWarning($"Unknown tag {pu.Tag3}: {pu}, {BitConverter.ToString(pu.Payload.ToByteArray()).Replace("-", "")}");
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
                
                //File.WriteAllBytes($@"c:\git\temp\zwift\companion-02-{(isTcp ? "tcp" : "udp")}\packet-{_udpPacketCounter:00000}.bin", buffer);
                //_udpPacketCounter++;   
            }
        }

        private void OnIncomingMessage116Event(Message116EventArgs eventArgs)
        {
            
        }
    }

    internal class Message116EventArgs
    {
        public Payload116 Message { get; set; }
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