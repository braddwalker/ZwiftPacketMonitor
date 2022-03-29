using System;

namespace ZwiftPacketMonitor {
    public class PlayerStateEventArgs : EventArgs {
        public PlayerState PlayerState {get; set;}
    }

    public class WaEventArgs : EventArgs {
        public WorldAttribute WorldAttribute { get; set; }
    }
    public class SegmentResultEventArgs : WaEventArgs {
        public SegmentResult SegmentResult { get; set;}
    }
    public class RideOnGivenEventArgs : WaEventArgs {
        public RideOn RideOn {get; set;}
    }
    public class SocialPlayerActionEventArgs : WaEventArgs {
        public SocialPlayerAction SocialPlayerAction { get; set;}
    }
    public class PlayerLeftWorldEventArgs : WaEventArgs {
        public PlayerLeftWorld PlayerLeftWorld { get; set;}
        public WA_TYPE WaType { get; set; }
    }
    public class EventProtobufEventArgs : WaEventArgs {
        public EventProtobuf EventProtobuf { get; set;}
    }
    public class PlayerJoinedEventArgs : WaEventArgs {
        public PlayerJoinedEvent PlayerJoinedEvent {get; set;}
    }
    public class PlayerLeftEventArgs : WaEventArgs {
        public PlayerLeftEvent PlayerLeftEvent {get; set;}
    }
    public class NotableEventArgs : WaEventArgs {
        public string NotableHexStr { get; set;}
        public Int64 PlayerId { get; set; }
    }
    public class GroupEventArgs : WaEventArgs {
        public string GeHexStr { get; set;}
        public string GeKindStr { get; set;}
        public Int64 PlayerId { get; set; }
    }
    public enum BikeAction 
    {
        BA_ELBOW = 0, BA_WAVE, BA_2, BA_RIDEON, 
        BA_HAMMER, BA_NICE, BA_BRING_IT, BA_TOAST, BA_BELL, BA_HOLIDAY_WAVE
    }
    public class BikeActionEventArgs : WaEventArgs {
        public string BaHexStr { get; set;}
        public BikeAction BikeAction { get; set;}
        public Int64 PlayerId { get; set; }
    }
    public class NoPayloadWaEventArgs : WaEventArgs {
        public WA_TYPE WaType { get; set;}
    }
    public class EventSubgroupPlacementsEventArgs : WaEventArgs {
        public EventSubgroupPlacements EvSubgroupPs { get; set;}
    }
    public class MessageWaEventArgs : WaEventArgs {
        public WA_TYPE WaType { get; set; }
        public string Message { get; set; }
    }
    public class FloatTimeWaEventArgs : WaEventArgs {
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
    public class FlagWaEventArgs : WaEventArgs {
        public FlagWaInfo FlagWaInfo { get; set; }
    }
}