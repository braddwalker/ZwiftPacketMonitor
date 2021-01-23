using System;
using System.Linq;
using PacketDotNet;
using Microsoft.Extensions.Logging;


namespace ZwiftPacketMonitor
{
    public class PayloadReadyEventArgs : EventArgs {
        public byte[] Payload { get; set; }
    }

    /// <summary>
    /// This helper class is used to identify and reassemble fragmented TCP payloads.
    /// </summary>
    public class PacketAssembler
    {
        public event EventHandler<PayloadReadyEventArgs> PayloadReady;

        private ILogger<PacketAssembler> logger;
        private int expectedLen;
        private byte[] payload;

        public PacketAssembler(ILogger<PacketAssembler> logger)
        {
            this.logger = logger;
        }

        private void OnPayloadReady(PayloadReadyEventArgs e)
        {
            EventHandler<PayloadReadyEventArgs> handler = PayloadReady;
            if (handler != null)
            {
                try {
                    handler(this, e);
                }
                catch {
                    // Don't let downstream exceptions bubble up
                }
                finally {
                    Reset();
                }
            }
        }  

        /// <summary>
        /// Processes the current packet. If this packet is part of a fragmented sequence,
        /// its payload will be added to the internal buffer until the entire sequence payload has
        /// been loaded. When the packet sequence has been fully loaded, the <c>PayloadReady</c> event is invoked.
        /// </summary>
        /// <param name="packet">The packet to process</param>
        public void Assemble(TcpPacket packet)
        {
            AssembleInternal(packet, packet.PayloadData);
        }

        private void AssembleInternal(TcpPacket packet, byte[] buffer)
        {
            // New packet sequence
            if (payload == null) 
            {
                payload = buffer;

                if (payload.Length > 2)
                {
                    // first 2 bytes are the total payload length
                    var payloadLenBytes = payload.Take(2).ToArray();
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(payloadLenBytes);
                    }

                    expectedLen = BitConverter.ToUInt16(payloadLenBytes, 0);
                }

                // trim off the header
                payload = payload.Skip(2).ToArray();

                if (payload.Length >= expectedLen)
                {
                    // Any bytes past the expectedLen are overflow from the next message
                    var overflow = payload.Skip(expectedLen).ToArray();
                    logger.LogDebug($"Complete packet - Expected: {expectedLen}, Actual: {payload.Length}, Push: {packet.Push}");

                    OnPayloadReady(new PayloadReadyEventArgs() { Payload = payload.ToArray() });

                    // See if the next packet sequence is comingled with this one
                    if (overflow.Length > 0)
                    {
                        logger.LogWarning($"OVERFLOW bytes detected - Len: {overflow.Length}, PayloadData: {BitConverter.ToString(overflow.ToArray()).Replace("-", "")}\n\r");
                        
                        // Start the process over as if this overflow data came in fresh from a packet
                        AssembleInternal(packet, overflow);
                    }
                }
                // the payload will be spread out across multiple packets
                else
                {
                    logger.LogDebug($"Fragmented packet detected - Expected: {expectedLen}, Actual: {payload.Length}, Push: {packet.Push}");
                }
            }
            // reconstructing a fragmented sequence
            else
            {
                // Append this packet's payload to the fragment one we're currently reassembling
                logger.LogDebug($"Combining packets - Expected: {expectedLen}, Actual: {payload.Length}, Packet: {packet.PayloadData.Length}, Push: {packet.Push}");
                payload = payload.Concat(packet.PayloadData).ToArray();

                if (payload.Length >= expectedLen)
                {
                    // Any bytes past the expectedLen are overflow from the next message
                    var overflow = payload.Skip(expectedLen).ToArray();
                    logger.LogDebug($"Fragmented packet completed!, Expected: {expectedLen}, Actual: {payload.Length}, Packet: {packet.PayloadData.Length}, Push: {packet.Push}");

                    // our original fragmented packet is ready to ship
                    OnPayloadReady(new PayloadReadyEventArgs() { Payload = payload.Take(expectedLen).ToArray() });

                    // See if the next packet sequence is comingled with this one
                    if (overflow.Length > 0)
                    {
                        logger.LogDebug($"OVERFLOW bytes detected - Len: {overflow.Length}, PayloadData: {BitConverter.ToString(overflow.ToArray()).Replace("-", "")}\n\r");
                        
                        // Start the process over as if this overflow data came in fresh from a packet
                        AssembleInternal(packet, overflow);
                    }
                }
            }
        }

        /// <summary>
        /// Resets the internal state to start over
        /// </summary>
        public void Reset()
        {
            payload = null;
            expectedLen = 0;
        }
    }
}