namespace ZwiftPacketMonitor
{
    /// <summary>
    /// This enumeration defines whether a given packet of data
    /// is incoming from the remote server, or outgoing from the local client
    /// </summary>
    public enum Direction
    {
        // Default value
        Unknown,
        // Incoming from the remote server
        Incoming,
        // Outgoing from the local client
        Outgoing
    }
}