using System;

namespace ZwiftPacketMonitor
{    public class PlayerStateEventArgs : EventArgs {
        public PlayerState PlayerState {get; set;}
        public DateTime EventDate {get; set;}
    }

    public class PlayerEnteredWorldEventArgs : EventArgs {
        public Payload105 PlayerUpdate {get; set;}
        public DateTime EventDate {get; set;}
    }

    public class RideOnGivenEventArgs : EventArgs {
        public Payload4 RideOn {get; set;}
        public DateTime EventDate {get; set;}
    }

    public class ChatMessageEventArgs : EventArgs {
        public Payload5 Message {get; set;}
        public DateTime EventDate {get; set;}
    }
}