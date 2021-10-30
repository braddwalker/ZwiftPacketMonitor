using System;

namespace ZwiftPacketMonitor
{
    public class RiderPositionEventArgs : EventArgs
    {
        public float Latitude { get; set; }
        public float Longitude { get; set; }
        public float Altitude { get; set; }
    }
}