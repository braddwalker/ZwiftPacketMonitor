using System;
using System.IO;
using System.Linq;
using System.Threading;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace ZwiftPacketMonitor
{
    public class CompanionPacketDecoder
    {
        private readonly ILogger<CompanionPacketDecoder> _logger;
        private readonly MessageDiagnostics _messageDiagnostics;

        public CompanionPacketDecoder(MessageDiagnostics messageDiagnostics, ILogger<CompanionPacketDecoder> logger)
        {
            _messageDiagnostics = messageDiagnostics;
            _logger = logger;
        }

        /// <summary>
        /// Raised when the Zwift desktop app indicates that a command has become available to the companion app
        /// </summary>
        public event EventHandler<CommandAvailableEventArgs> CommandAvailable;
        /// <summary>
        /// Raised when the companion app sends a command to the desktop app
        /// </summary>
        public event EventHandler<CommandSentEventArgs> CommandSent;


        public void DecodeOutgoing(byte[] buffer, uint sequenceNumber)
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

                _messageDiagnostics.StoreMessageType(10, buffer, Direction.Outgoing, sequenceNumber);

                return;
            }

            if (riderMessage.Details != null)
            {
                //_messageDiagnostics.StoreMessageType(riderMessage.Details.Type, buffer, Direction.Outgoing, sequenceNumber);

                switch (riderMessage.Details.Type)
                {
                    case 14:
                        _logger.LogInformation("Sent a type 14 command but no clue what it is");

                        //_messageDiagnostics.StoreMessageType(riderMessage.Details.Type, buffer, Direction.Outgoing, sequenceNumber);

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
                        return;
                    // Seems to be a command form the companion app to the desktop app
                    case 22 when riderMessage.Details.HasCommandType:
                        OnCommandSent(riderMessage.Details.CommandType);
                        //_messageDiagnostics.StoreMessageType(22, buffer, Direction.Outgoing, sequenceNumber, 200);
                        return;
                    case 28:
                        _logger.LogInformation("Possibly sent our own rider id sync command");

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

                        //_messageDiagnostics.StoreMessageType(riderMessage.Details.Type, buffer, Direction.Outgoing, sequenceNumber);

                        return;
                }
            }

            _logger.LogWarning("Sent a message that we don't recognize yet");

            _messageDiagnostics.StoreMessageType(999, buffer, Direction.Outgoing, sequenceNumber);
        }

        public void DecodeIncoming(byte[] buffer, uint sequenceNumber)
        {
            var storeEntireMessage = false;

            var packetData = ZwiftAppToCompanion.Parser.ParseFrom(buffer);

            foreach (var item in packetData.Items)
            {
                var byteArray = item.ToByteArray();

                //_messageDiagnostics.StoreMessageType(item.Type, byteArray, Direction.Incoming, sequenceNumber);

                switch (item.Type)
                {
                    case 1:
                        // Empty, ignore this
                        break;
                    case 2:
                        var powerUp = ZwiftAppToCompanionPowerUpMessage.Parser.ParseFrom(byteArray);

                        _logger.LogInformation("Received power up {power_up}", powerUp.PowerUp);
                        break;
                    case 3:
                        _logger.LogDebug("Received a type 3 message that we don't understand yet");
                        break;
                    case 4:
                        var buttonMessage = ZwiftAppToCompanionButtonMessage.Parser.ParseFrom(byteArray);
                        
                        OnCommandAvailable(buttonMessage.TypeId, buttonMessage.Title);
                        
                        _messageDiagnostics.StoreMessageType(4, byteArray, Direction.Incoming, sequenceNumber, 200);

                        break;
                    case 9:
                        _logger.LogDebug("Received a type 9 message that we don't understand yet");
                        break;
                    // Activity details?
                    case 13:
                        var activityDetails =
                            ZwiftAppToCompanionActivityDetailsMessage.Parser.ParseFrom(byteArray);

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
                                        // This seems to be us
                                        if (s.Index == 10)
                                        {
                                            var rider = s.Riders.Single();
                                            var subject = $"{rider.Description} ({rider.RiderId})";
                                            _logger.LogDebug("Received our own rider information: {subject}", subject);

                                            var line = $"{rider.SomeData.Latitude};{rider.SomeData.Tag2};{rider.SomeData.Longitude}\n";

                                            File.AppendAllLines(
                                                @"c:\git\temp\zwift\companion-to-app-stream-05-myposition.csv", 
                                                new [] {line});
                                        }

                                        foreach (var rider in s.Riders)
                                        {
                                            var subject = $"{rider.Description} ({rider.RiderId})";

                                            _logger.LogDebug("Received rider information: {subject}", subject);
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
                                break;
                            case 21:
                                _logger.LogDebug("Received a type 21 message that we don't understand yet");
                                break;
                            case 23:
                                _logger.LogDebug("Received a type 21 message that we don't understand yet");
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

                        _messageDiagnostics.StoreMessageType(item.Type, byteArray, Direction.Incoming, sequenceNumber);

                        break;
                }
            }

            //if (storeEntireMessage)
            //{
            //    _messageDiagnostics.StoreMessageType(0, buffer, Direction.Incoming, sequenceNumber);
            //}
        }

        private void OnCommandSent(uint numericalCommandType)
        {
            var commandType = CommandType.Unknown;

            try
            {
                commandType = (CommandType)numericalCommandType;
            }
            catch 
            {
                // Nop
            }

            if (commandType == CommandType.Unknown)
            {
                _logger.LogWarning("Sent unknown command {type}", numericalCommandType);
            }

            try
            {
                CommandSent?.Invoke(this, new CommandSentEventArgs { CommandType =  commandType });
            }
            catch
            {
                // Ignore exceptions from event handlers.
            }
        }

        private void OnCommandAvailable(uint numericalCommandType, string description)
        {
            var commandType = CommandType.Unknown;

            try
            {
                commandType = (CommandType)numericalCommandType;
            }
            catch 
            {
                // Nop
            }

            if (commandType == CommandType.Unknown)
            {
                _logger.LogWarning("Did not recognise command {type} ({description})", numericalCommandType, description);
            }

            try
            {
                CommandAvailable?.Invoke(this, new CommandAvailableEventArgs { CommandType =  commandType });
            }
            catch
            {
                // Ignore exceptions from event handlers.
            }
        }
    }
}
