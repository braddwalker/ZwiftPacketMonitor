using System;

namespace ZwiftPacketMonitor {
    public class PlayerStateEventArgs : EventArgs {
        public PlayerState PlayerState {get; set;}
    }

    public class SegmentResultEventArgs : EventArgs {
        public SegmentResult SegmentResult { get; set;}
    }

    public class RideOnGivenEventArgs : EventArgs {
        public RideOn RideOn {get; set;}
    }

    public class SocialPlayerActionEventArgs : EventArgs {
        public SocialPlayerAction SocialPlayerAction { get; set;}
    }

    public class PlayerLeftWorldEventArgs : EventArgs {
        public PlayerLeftWorld PlayerLeftWorld { get; set;}
        public WA_TYPE WaType { get; set; }
    }

    public class EventProtobufEventArgs : EventArgs {
        public EventProtobuf EventProtobuf { get; set;}
    }

    public class PlayerJoinedEventArgs : EventArgs {
        public PlayerJoinedEvent PlayerJoinedEvent {get; set;}
    }

    public class PlayerLeftEventArgs : EventArgs {
        public PlayerLeftEvent PlayerLeftEvent {get; set;}
    }

    public class EventInviteEventArgs : EventArgs {
        public EventInviteProto EventInviteProto { get; set;}
    }

    public class NotableEventArgs : EventArgs {
        public string NotableHexStr { get; set;}
        public Int64 PlayerId { get; set; }
    }

    public class GroupEventArgs : EventArgs {
        public string GeHexStr { get; set;}
        public string GeKindStr { get; set;}
        public Int64 PlayerId { get; set; }
    }
    public enum BikeAction 
    {
        BA_ELBOW = 0, BA_WAVE, BA_2, BA_RIDEON, 
        BA_HAMMER, BA_NICE, BA_BRING_IT, BA_TOAST, BA_BELL, BA_HOLIDAY_WAVE
    }
    public class BikeActionEventArgs : EventArgs {
        public string BaHexStr { get; set;}
        public BikeAction BikeAction { get; set;}
        public Int64 PlayerId { get; set; }
    }

    public class NoPayloadWaEventArgs : EventArgs {
        public WA_TYPE WaType { get; set;}
    }

    public class EventSubgroupPlacementsEventArgs : EventArgs {
        public EventSubgroupPlacements EvSubgroupPs { get; set;}
    }

    public class MessageWaEventArgs : EventArgs
    {
        public WA_TYPE WaType { get; set; }
        public string Message { get; set; }
    }

    public class FloatTimeWaEventArgs : EventArgs
    {
        public WA_TYPE WaType { get; set; }
        public float FloatTime { get; set; }
    }

    public class FlagWaInfo {
        public Int64 networkId, playerId;
        public double doubleVal;
        public int flag;
        public FlagWaInfo(byte[] src) {
            networkId = BitConverter.ToInt64(src, 0);
            playerId = BitConverter.ToInt64(src, 8);
            doubleVal = BitConverter.ToDouble(src, 16);
            flag = BitConverter.ToInt32(src, 24);
        }
        public override string ToString() {
            //networkId must be  == 1 (also seen 4296802305), flag: 4 -> Sandbagger, otherwise: Cheater. Double looks like world_time/1000.
            return $"networkId: {networkId}, playerId: {playerId}, doubleVal: {doubleVal}, flag: {flag}";
        }
    }

    public class FlagWaEventArgs : EventArgs
    {
        public FlagWaInfo FlagWaInfo { get; set; }
    }
}