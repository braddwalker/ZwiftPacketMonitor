using System;

namespace ZwiftPacketMonitor
{
    public class CommandSentEventArgs : EventArgs
    {
        public CommandType CommandType { get; set; }
    }
}