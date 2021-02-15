using System;

namespace ZwiftPacketMonitor
{    public class PlayerStateEventArgs : EventArgs {
        public PlayerState PlayerState {get; set;}
    }

    public class PlayerEnteredWorldEventArgs : EventArgs {
        public Payload105 PlayerUpdate {get; set;}
    }

    public class RideOnGivenEventArgs : EventArgs {
        public Payload4 RideOn {get; set;}
    }

    public class ChatMessageEventArgs : EventArgs {
        public Payload5 Message {get; set;}
    }

    public class PlayerTimeSyncEventArgs : EventArgs {
        public TimeSync TimeSync {get; set;} 
    }

    public class MeetupEventArgs : EventArgs {
        public Meetup Meetup {get; set;}
    }

    public class EventPositionsEventArgs : EventArgs {
        public EventPositions EventPositions {get; set;}
    }
}