using System;
using System.Linq;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace ZwiftPacketMonitor
{
    public class CompanionPacketDecoder
    {
        private readonly ILogger<CompanionPacketDecoder> _logger;
        private readonly IMessageWriter _invidividualFileMessageWriter;

        public CompanionPacketDecoder(IMessageWriter invidividualFileMessageWriter, ILogger<CompanionPacketDecoder> logger)
        {
            _invidividualFileMessageWriter = invidividualFileMessageWriter ?? new NopMessageWriter();
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

        /// <summary>
        /// Raised when the position of the rider is sent to the companion app
        /// </summary>
        public event EventHandler<RiderPositionEventArgs> RiderPosition;

        /// <summary>
        /// Raised when a power up is rewarded in the game
        /// </summary>
        public event EventHandler<PowerUpEventArgs> PowerUp;

        /// <summary>
        /// Raised when a heart beat message is received
        /// </summary>
        public event EventHandler<EventArgs> HeartBeat;

        /// <summary>
        /// Raised when activity details are received
        /// </summary>
        public event EventHandler<ActivityDetailsEventArgs> ActivityDetails;

        public void DecodeOutgoing(byte[] buffer, uint sequenceNumber)
        {
            var message = ZwiftCompanionToApp.Parser.ParseFrom(buffer);

            // This is for messages that have only tag1 and tag10
            // and we can't figure out based on that alone if there
            // is any proper data in it...
            if (buffer.Length <= 10)
            {
                OnHeartBeat();
                return;
            }

            var riderMessage = ZwiftCompanionToAppRiderMessage.Parser.ParseFrom(buffer);

            if (riderMessage.Details == null && message.Tag10 == 0)
            {
                var typeTag10Zero = ZwiftCompanionToAppMessageTag10Zero.Parser.ParseFrom(buffer);
                var clockTime = DateTimeOffset.FromUnixTimeSeconds((long)typeTag10Zero.ClockTime);
                _logger.LogDebug($"Sent a tag 10 = 0 type message with timestamp {clockTime}");

                return;
            }

            if (riderMessage.Details != null)
            {
                switch (riderMessage.Details.Type)
                {
                    case 14:
                        _logger.LogDebug("Sent a type 14 message");
                        return;
                    case 16:
                        {
                            // I don't think this is a ride-on because it happens _a lot_....

                            var rideOn = ZwiftCompanionToAppRideOnMessage.Parser.ParseFrom(riderMessage.Details.ToByteArray());

                            _logger.LogDebug(
                                "Possibly sent a ride-on message to {other_rider_id}",
                                rideOn.OtherRiderId);

                            return;
                        }
                    case 20:
                        _logger.LogDebug("Sent a type 20 message");
                        return;
                    case 22 when riderMessage.Details.HasCommandType:
                        OnCommandSent(riderMessage.Details.CommandType);
                        return;
                    case 28:
                        _logger.LogDebug("Possibly sent our own rider id sync command");
                        return;
                    case 29 when riderMessage.Details.Data.Tag1 == 4:
                        {
                            var deviceInfoVersion = ZwiftCompanionToAppDeviceInfoMessage.Parser.ParseFrom(buffer).DeviceInfo
                                .Device.Version;

                            var deviceString =
                                $"{deviceInfoVersion.Os} ({deviceInfoVersion.OsVersion}) on {deviceInfoVersion.Device} {deviceInfoVersion.AppVersion}";

                            _logger.LogDebug($"Sent device info to the Zwift Desktop app: {deviceString}");

                            return;
                        }
                    case 29 when riderMessage.Details.Data.Tag1 == 15:
                        {
                            var endActivity =
                                ZwiftCompanionToAppEndActivityMessage.Parser.ParseFrom(riderMessage.Details.Data.ToByteArray());

                            var subject = $"{endActivity.Data.ActivityName}";

                            _logger.LogDebug($"Sent (possible) end activity command: {subject}");

                            return;
                        }
                    default:
                        _logger.LogDebug($"Found a rider detail message of type {riderMessage.Details.Type} that we don't understand");

                        _invidividualFileMessageWriter.StoreMessageType(riderMessage.Details.Type, buffer, Direction.Outgoing, sequenceNumber);

                        return;
                }
            }

            _logger.LogWarning("Sent a message that we don't recognize yet");

            _invidividualFileMessageWriter.StoreMessageType(999, buffer, Direction.Outgoing, sequenceNumber);
        }

        public void DecodeIncoming(byte[] buffer, uint sequenceNumber)
        {
            var packetData = ZwiftAppToCompanion.Parser.ParseFrom(buffer);

            foreach (var item in packetData.Items)
            {
                var byteArray = item.ToByteArray();

                switch (item.Type)
                {
                    case 1:
                    case 3:
                    case 6:
                        // Empty, ignore this
                        break;
                    case 2:
                        var powerUp = ZwiftAppToCompanionPowerUpMessage.Parser.ParseFrom(byteArray);
                        
                        OnPowerUp(powerUp.PowerUp);

                        break;
                    case 4:
                        var buttonMessage = ZwiftAppToCompanionButtonMessage.Parser.ParseFrom(byteArray);

                        OnCommandAvailable(buttonMessage.TypeId, buttonMessage.Title);

                        break;
                    case 9:
                        _logger.LogDebug("Received a type 9 message that we don't understand yet");
                        break;
                    case 13:
                        var activityDetails = ZwiftAppToCompanionActivityDetailsMessage.Parser.ParseFrom(byteArray);

                        DecodeIncomingActivityDetailsMessage(sequenceNumber, activityDetails);

                        break;
                    default:
                        _logger.LogWarning($"Received type {item.Type} message");

                        _invidividualFileMessageWriter.StoreMessageType(item.Type, byteArray, Direction.Incoming, sequenceNumber);

                        break;
                }
            }
        }

        private void DecodeIncomingActivityDetailsMessage(
            uint sequenceNumber,
            ZwiftAppToCompanionActivityDetailsMessage activityDetails)
        {
            switch (activityDetails.Details.Type)
            {
                case 3:
                    OnActivityDetails(activityDetails.Details.Data.ActivityId);
                    break;
                case 5:
                    {
                        // This item type either has our position or that of a bunch of others
                        // so let's first see if we can deal with our position first.
                        if (activityDetails.Details.RiderData.Sub.Count == 1 &&
                            activityDetails.Details.RiderData.Sub[0].Index == 10)
                        {
                            var rider = activityDetails.Details.RiderData.Sub[0].Riders[0];

                            OnRiderPosition(
                                rider.Position.Latitude,
                                rider.Position.Longitude,
                                rider.Position.Altitude);

                            break;
                        }

                        foreach (var s in activityDetails.Details.RiderData.Sub)
                        {
                            if (s?.Riders != null && s.Riders.Any())
                            {
                                foreach (var rider in s.Riders)
                                {
                                    var subject = $"{rider.Description} ({rider.RiderId})";

                                    _logger.LogDebug($"Received rider information: {subject}");
                                }
                            }
                        }

                        break;
                    }
                case 6:
                    // Ignore
                    break;
                case 7:
                    // Ignore, comes by lots of times
                    break;
                case 10:
                    // Ignore, this has very limited data that I have no idea about what it means
                    break;
                case 18:
                    // Ignore, this has very limited data that I have no idea about what it means
                    break;
                case 17:
                case 19:
                    // Rider nearby?
                    {
                        var rider = activityDetails
                            .Details
                            ?.OtherRider;

                        if (rider != null)
                        {
                            var subject = $"{rider.FirstName?.Trim()} {rider.LastName?.Trim()} ({rider.RiderId})";

                            _logger.LogDebug($"Received rider nearby position for {subject}");
                        }

                        break;
                    }
                case 20:
                    // Ignore, contains very little data and is similar to type 21
                    break;
                case 21:
                    // Ignore, contains very little data
                    break;
                default:
                    _logger.LogDebug($"Received a activity details subtype with {activityDetails.Details.Type} that we don't understand yet");

                    _invidividualFileMessageWriter.StoreMessageType(activityDetails.Details.Type, activityDetails.ToByteArray(), Direction.Incoming,
                        sequenceNumber);
                    break;
            }
        }

        private void OnActivityDetails(ulong activityId)
        {
            try
            {
                ActivityDetails?.Invoke(this, new ActivityDetailsEventArgs
                {
                    ActivityId = activityId
                });
            }
            catch
            {
                // Ignore exceptions from event handlers.
            }
        }

        private void OnPowerUp(string type)
        {
            try
            {
                PowerUp?.Invoke(this, new PowerUpEventArgs { Type = type });
            }
            catch
            {
                // Ignore exceptions from event handlers.
            }
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
                _logger.LogWarning($"Sent unknown command {numericalCommandType}");
            }

            try
            {
                CommandSent?.Invoke(this, new CommandSentEventArgs { CommandType = commandType });
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
                _logger.LogWarning($"Did not recognise command {numericalCommandType} ({description})");
            }

            try
            {
                CommandAvailable?.Invoke(this, new CommandAvailableEventArgs { CommandType = commandType });
            }
            catch
            {
                // Ignore exceptions from event handlers.
            }
        }

        private void OnRiderPosition(float latitude, float longitude, float altitude)
        {
            try
            {
                RiderPosition?.Invoke(this,
                    new RiderPositionEventArgs { Latitude = latitude, Longitude = longitude, Altitude = altitude });
            }
            catch
            {
                // Ignore exceptions from event handlers.
            }
        }

        private void OnHeartBeat()
        {
            try
            {
                HeartBeat?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                // Ignore exceptions from event handlers.
            }
        }
    }
}
