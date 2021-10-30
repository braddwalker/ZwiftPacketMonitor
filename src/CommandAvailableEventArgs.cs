using System;

namespace ZwiftPacketMonitor
{
    public class CommandAvailableEventArgs : EventArgs
    {
        public CommandType CommandType { get; set; }
    }
}