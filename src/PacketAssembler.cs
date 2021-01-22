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

                // PSH flag indicates the payload is wholly contained in this packet
                if (payload.Length == expectedLen)
                //if (packet.Push)
                {
                    logger.LogDebug($"Complete packet - Expected: {expectedLen}, Actual: {payload.Length}, Push: {packet.Push}");

                    OnPayloadReady(new PayloadReadyEventArgs() { Payload = payload.ToArray() });

                    // clear out state for the next sequence
                    Reset();
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

                var overflow = new byte[0];

                if (payload.Length >= expectedLen)
                {
                    overflow = payload.Skip(expectedLen).ToArray();
                    logger.LogDebug($"Fragmented packet completed!, Expected: {expectedLen}, Actual: {payload.Length}, Packet: {packet.PayloadData.Length}, Push: {packet.Push}");

                    OnPayloadReady(new PayloadReadyEventArgs() { Payload = payload.Take(expectedLen).ToArray() });
                    Reset();
                }

                // See if the next packet sequence is comingled with this one
                if (overflow.Length > 0)
                {
                    logger.LogWarning($"OVERFLOW bytes detected - Len: {overflow.Length - expectedLen}, PayloadData: {BitConverter.ToString(overflow.ToArray()).Replace("-", "")}\n\r");

                    Reset();
                    AssembleInternal(packet, overflow);
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