using System;

namespace ZwiftPacketMonitor
{
    public class ActivityDetailsEventArgs : EventArgs
    {
        public ulong ActivityId { get; set; }
    }
}