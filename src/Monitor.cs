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
using Google.Protobuf;
using Google.Protobuf.Reflection;
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
        public bool IsRunning { get; private set; }

        private LibPcapLiveDevice _device;
        private readonly ILogger<Monitor> _logger;
        private readonly PacketAssembler _packetAssembler;
        private readonly PacketAssembler _companionPacketAssemblerPcToApp;
        private readonly PacketAssembler _companionPacketAssemblerAppToPc;
        private readonly Dictionary<string, int> _messageTypeCounters = new Dictionary<string, int>();
        private DateTime? _offset;

        /// <summary>
        /// Creates a new instance of the monitor class.
        /// </summary>
        public Monitor(ILogger<Monitor> logger, PacketAssembler packetAssembler, PacketAssembler companionPacketAssemblerPcToApp, PacketAssembler companionPacketAssemblerAppToPc)
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
                DeserializeAndDispatchCompanion(e.Payload, Direction.Incoming, e.SequenceNumber);
            };

            _companionPacketAssemblerAppToPc = companionPacketAssemblerAppToPc;
            _companionPacketAssemblerAppToPc.PayloadReady += (s, e) =>
            {
                // Only incoming TCP payloads are coming through here
                DeserializeAndDispatchCompanion(e.Payload, Direction.Outgoing, e.SequenceNumber);
            };

        }

        private void OnIncomingEventPositionsEvent(EventPositionsEventArgs e)
        {
            var handler = IncomingEventPositionsEvent;
            if (handler != null)
            {
                try
                {
                    handler(this, e);
                }
                catch
                {
                    // Don't let downstream exceptions bubble up
                }
            }
        }

        private void OnIncomingMeetupEvent(MeetupEventArgs e)
        {
            var handler = IncomingMeetupEvent;
            if (handler != null)
            {
                try
                {
                    handler(this, e);
                }
                catch
                {
                    // Don't let downstream exceptions bubble up
                }
            }
        }

        private void OnIncomingChatMessageEvent(ChatMessageEventArgs e)
        {
            var handler = IncomingChatMessageEvent;
            if (handler != null)
            {
                try
                {
                    handler(this, e);
                }
                catch
                {
                    // Don't let downstream exceptions bubble up
                }
            }
        }

        private void OnIncomingRideOnGivenEvent(RideOnGivenEventArgs e)
        {
            var handler = IncomingRideOnGivenEvent;
            if (handler != null)
            {
                try
                {
                    handler(this, e);
                }
                catch
                {
                    // Don't let downstream exceptions bubble up
                }
            }
        }

        private void OnIncomingPlayerEnteredWorldEvent(PlayerEnteredWorldEventArgs e)
        {
            var handler = IncomingPlayerEnteredWorldEvent;
            if (handler != null)
            {
                try
                {
                    handler(this, e);
                }
                catch
                {
                    // Don't let downstream exceptions bubble up
                }
            }
        }

        private void OnIncomingPlayerEvent(PlayerStateEventArgs e)
        {
            var handler = IncomingPlayerEvent;
            if (handler != null)
            {
                try
                {
                    handler(this, e);
                }
                catch
                {
                    // Don't let downstream exceptions bubble up
                }
            }
        }

        private void OnOutgoingPlayerEvent(PlayerStateEventArgs e)
        {
            var handler = OutgoingPlayerEvent;
            if (handler != null)
            {
                try
                {
                    handler(this, e);
                }
                catch
                {
                    // Don't let downstream exceptions bubble up
                }
            }
        }

        private void OnIncomingPlayerTimeSyncEvent(PlayerTimeSyncEventArgs e)
        {
            var handler = IncomingPlayerTimeSyncEvent;
            if (handler != null)
            {
                try
                {
                    handler(this, e);
                }
                catch
                {
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
                await Task.Run(() =>
                {
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
                    if (_offset == null)
                    {
                        _offset = e.Header.Timeval.Date;
                    }

                    tcpPacket.SequenceNumber = (uint)(e.Header.Timeval.Date - _offset.Value).TotalMilliseconds;

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

                        if (packetBytes[skip] == 0x08)
                        {
                            // NOOP, as the protobuf payload looks like it starts after the initial skip estimate
                        }
                        else if (packetBytes[0] == 0x08)
                        {
                            // protobuf payload starts at the beginning
                            skip = 0;
                        }
                        else
                        {
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

        private void DeserializeAndDispatchCompanion(byte[] buffer, Direction direction, uint sequenceNumber)
        {
            if (direction == Direction.Incoming)
            {
                DeserializeAndDispatchIncomingMessage(buffer, direction, sequenceNumber);
            }
            else if (direction == Direction.Outgoing)
            {
                DeserializeAndDispatchOutgoingMessage(buffer, direction, sequenceNumber);
            }
        }

        private void DeserializeAndDispatchOutgoingMessage(byte[] buffer, Direction direction, uint sequenceNumber)
        {
            var message = ZwiftCompanionToApp.Parser.ParseFrom(buffer);

            // This is for messages that have only tag1 and tag10
            // and we can't figure out based on that alone if there
            // is any proper data in it...
            if (buffer.Length <= 10)
            {
                _logger.LogDebug("Found a heartbeat message");

                return;
            }

            var riderMessage = ZwiftCompanionToAppRiderMessage.Parser.ParseFrom(buffer);

            if (riderMessage.Details == null && message.Tag10 == 0)
            {
                var typeTag10Zero = ZwiftCompanionToAppMessageTag10Zero.Parser.ParseFrom(buffer);
                var clockTime = DateTimeOffset.FromUnixTimeSeconds((long)typeTag10Zero.ClockTime);
                _logger.LogDebug("Sent a tag 10 = 0 type message with timestamp {clock_time}", clockTime);

                StoreMessageType(100, buffer, direction, sequenceNumber);

                return;
            }

            if (riderMessage.Details != null)
            {
                switch (riderMessage.Details.Type)
                {
                    case 14:
                        _logger.LogInformation("Sent a type 14 command but no clue what it is");

                        StoreMessageType(riderMessage.Details.Type, buffer, direction, sequenceNumber);

                        return;
                    case 16:
                        // I don't think this is a ride-on because it happens _a lot_....

                        var rideOn = ZwiftCompanionToAppRideOnMessage.Parser.ParseFrom(riderMessage.Details.ToByteArray());

                        _logger.LogDebug(
                            "Possibly sent a ride-on message to {other_rider_id}",
                            rideOn.OtherRiderId);

                        return;
                    case 20:
                        _logger.LogInformation("Sent a type 20 command but no idea what it is");

                        StoreMessageType(riderMessage.Details.Type, buffer, direction, sequenceNumber);

                        return;
                    // Seems to be a command form the companion app to the desktop app
                    case 22 when riderMessage.Details.HasCommandType:
                        switch (riderMessage.Details.CommandType) // Tag10 seems to be the type of command
                        {
                            case 6:
                                _logger.LogInformation("Possibly sent RIDE ON command");
                                return;
                            case 1010:
                                _logger.LogInformation("Sent TURN LEFT command");

                                StoreMessageType(riderMessage.Details.Type, buffer, direction, sequenceNumber, 40);
                                return;
                            case 1011:
                                _logger.LogInformation("Sent GO STRAIGHT command");

                                StoreMessageType(riderMessage.Details.Type, buffer, direction, sequenceNumber, 40);
                                return;
                            case 1012:
                                _logger.LogInformation("Sent TURN RIGHT command");

                                StoreMessageType(riderMessage.Details.Type, buffer, direction, sequenceNumber, 40);
                                return;
                        }

                        break;
                    case 28:
                        _logger.LogInformation("Possibly sent our own rider id sync command");

                        StoreMessageType(riderMessage.Details.Type, buffer, direction, sequenceNumber, 40);

                        return;
                    // Device info
                    case 29 when riderMessage.Details.Data.Tag1 == 4:
                        {
                            var deviceInfoVersion = ZwiftCompanionToAppDeviceInfoMessage.Parser.ParseFrom(buffer).DeviceInfo
                                .Device.Version;

                            var deviceString =
                                $"{deviceInfoVersion.Os} ({deviceInfoVersion.OsVersion}) on {deviceInfoVersion.Device} {deviceInfoVersion.AppVersion}";

                            _logger.LogInformation("Sent device info to the Zwift Desktop app: {data}", deviceString);

                            return;
                        }
                    // End activity?
                    case 29 when riderMessage.Details.Data.Tag1 == 15:
                        {
                            var endActivity =
                                ZwiftCompanionToAppEndActivityMessage.Parser.ParseFrom(riderMessage.Details.Data.ToByteArray());

                            var subject = $"{endActivity.Data.ActivityName}";

                            _logger.LogInformation("Sent (possible) end activity command: {subject}", subject);

                            return;
                        }
                    default:
                        _logger.LogInformation("Found a rider detail message of type: " + riderMessage.Details.Type);

                        StoreMessageType(riderMessage.Details.Type, buffer, direction, sequenceNumber);

                        return;
                }
            }

            _logger.LogWarning("Sent a message that we don't recognize yet");

            StoreMessageType(999, buffer, direction, sequenceNumber);
        }

        private void DeserializeAndDispatchIncomingMessage(byte[] buffer, Direction direction, uint sequenceNumber)
        {
            var storeEntireMessage = false;

            var packetData = ZwiftAppToCompanion.Parser.ParseFrom(buffer);

            foreach (var item in packetData.Items)
            {
                switch (item.Type)
                {
                    case 1:
                        // Empty, ignore this
                        break;
                    case 2:
                        var powerUp = ZwiftAppToCompanionPowerUpMessage.Parser.ParseFrom(item.ToByteArray());

                        _logger.LogInformation("Received power up {power_up}", powerUp.PowerUp);
                        break;
                    case 3:
                        _logger.LogDebug("Received a type 3 message that we don't understand yet");
                        //StoreMessageType(item.Type, item.ToByteArray(), direction, sequenceNumber, 40);
                        break;
                    case 4:
                        var buttonMessage = ZwiftAppToCompanionButtonMessage.Parser.ParseFrom(item.ToByteArray());

                        DispatchButtonMessage(direction, buttonMessage, item, sequenceNumber);
                        break;
                    case 9:
                        _logger.LogDebug("Received a type 9 message that we don't understand yet");
                        //StoreMessageType(item.Type, item.ToByteArray(), direction, sequenceNumber, 40);
                        break;
                    // Activity details?
                    case 13:
                        var activityDetails =
                            ZwiftAppToCompanionActivityDetailsMessage.Parser.ParseFrom(item.ToByteArray());

                        switch (activityDetails.Details.Type)
                        {
                            case 3:
                                _logger.LogInformation(
                                    "Received activity details, activity id {activity_id}",
                                    activityDetails.Details.Data.ActivityId);
                                break;
                            case 5:
                                {
                                    foreach (var s in activityDetails.Details.RiderData.Sub)
                                    {
                                        if (s?.Riders != null && s.Riders.Any())
                                        {
                                            foreach (var rider in s.Riders)
                                            {
                                                var subject = $"{rider.Description} ({rider.RiderId})";

                                                _logger.LogDebug("Received our own rider information: {subject}", subject);
                                                // It seems that this data doesn't ever change during the session....
                                            }
                                        }
                                        else
                                        {
                                            _logger.LogDebug("Received some position information without rider details");
                                        }
                                    }

                                    break;
                                }
                            case 6:
                                // This contains an empty string but no idea what it means
                                _logger.LogDebug("Received a activity details subtype with {type}",
                                    activityDetails.Details.Type);
                                break;
                            case 7:
                                _logger.LogDebug("Received a type 7 message that we don't understand yet. Tag 11[].8.1 contains a rider id"); // No not really
                                break;
                            case 10:
                                _logger.LogDebug("Received a type 10 message that we don't understand yet");
                                break;
                            case 17:
                            case 18:
                            case 19:
                                // Rider nearby?
                                {
                                    var rider = activityDetails
                                        .Details
                                        ?.OtherRider;

                                    if (rider != null)
                                    {
                                        var subject = $"{rider.FirstName?.Trim()} {rider.LastName?.Trim()} ({rider.RiderId})";

                                        _logger.LogDebug("Received rider nearby position for {subject}", subject);
                                    }

                                    break;
                                }
                            case 20:
                                _logger.LogDebug("Received a type 20 message that we don't understand yet");
                                StoreMessageType(20, item.ToByteArray(), direction, sequenceNumber, 50);
                                break;
                            case 21:
                                _logger.LogDebug("Received a type 21 message that we don't understand yet");
                                StoreMessageType(21, item.ToByteArray(), direction, sequenceNumber, 50);
                                break;
                            case 23:
                                _logger.LogDebug("Received a type 21 message that we don't understand yet");
                                StoreMessageType(23, item.ToByteArray(), direction, sequenceNumber, 50);
                                storeEntireMessage = true;
                                break;
                            default:
                                _logger.LogDebug("Received a activity details subtype with {type}",
                                    activityDetails.Details.Type);
                                storeEntireMessage = true;
                                break;
                        }

                        break;
                    default:
                        _logger.LogWarning("Received type {type} message", item.Type);

                        storeEntireMessage = true;

                        StoreMessageType(item.Type, item.ToByteArray(), direction, sequenceNumber);

                        break;
                }
            }

            if (storeEntireMessage)
            {
                StoreMessageType(0, buffer, direction, sequenceNumber);
            }
        }

        private void DispatchButtonMessage(
            Direction direction, 
            ZwiftAppToCompanionButtonMessage buttonMessage,
            ZwiftAppToCompanion.Types.SubItem item, 
            uint sequenceNummber)
        {
            switch (buttonMessage.TypeId)
            {
                // Elbow flick
                case 4:
                    // Would we get this if someone is drafting us?
                    _logger.LogDebug("Received ELBOW FLICK button available");
                    break;
                // Wave
                case 5:
                    _logger.LogDebug("Received WAVE button available");
                    break;
                // Ride on
                case 6:
                    _logger.LogDebug("Received RIDE ON button available");
                    break;
                case 23:
                    // It appears value 23 is something empty
                    break;
                // Turn Left
                case 1010:
                    _logger.LogDebug("Received TURN LEFT button available");
                    //StoreMessageType(item.Type, item.ToByteArray(), direction, sequenceNummber, 40);
                    break;
                // Go Straight
                case 1011:
                    _logger.LogDebug("Received GO STRAIGHT button available");
                    //StoreMessageType(item.Type, item.ToByteArray(), direction, sequenceNummber, 40);
                    break;
                // Turn right
                case 1012:
                    _logger.LogDebug("Received TURN RIGHT button available");
                    //StoreMessageType(item.Type, item.ToByteArray(), direction, sequenceNummber, 40);
                    break;
                // Discard leightweight
                case 1030:
                    _logger.LogDebug("Received DISCARD AERO button available");
                    break;
                case 1034:
                    _logger.LogDebug("Received DISCARD LIGHTWEIGHT button available");
                    break;
                // POWER GRAPH
                case 1060:
                    _logger.LogDebug("Received POWER GRAPH button available");
                    break;
                // HUD
                case 1081:
                    _logger.LogDebug("Received HUD button available");
                    break;
                default:
                    _logger.LogWarning(
                        "Received a button available that we don't recognise {type}",
                        buttonMessage.TypeId);

                    StoreMessageType(item.Type, item.ToByteArray(), direction, sequenceNummber, 40);

                    break;
            }
        }

        private void StoreMessageType(
            uint messageType, 
            byte[] buffer, 
            Direction direction, 
            uint sequenceNummber,
            int maxNumberOfMessages = 10)
        {
            var basePath = $"c:\\git\\temp\\zwift\\companion-04-tcp";

            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            var type = $"{direction.ToString().ToLower()}-{messageType}";

            if (!_messageTypeCounters.ContainsKey(type))
            {
                _messageTypeCounters.Add(type, 0);
            }

            if (_messageTypeCounters[type] < maxNumberOfMessages)
            {
                File.WriteAllBytes(
                    $"{basePath}\\{sequenceNummber:000000}-{direction.ToString().ToLower()}-{messageType:000}.bin",
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