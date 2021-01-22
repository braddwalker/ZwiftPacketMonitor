using System;
using System.Linq;
using PacketDotNet;
using Microsoft.Extensions.Logging;


namespace ZwiftPacketMonitor
{
    public class PacketAssemblyResult
    {
        /// <summary>
        /// A flag indicating whether the payload is ready to be consumed. False if
        /// the fragmented segment is still being assembled
        /// </summary>
        /// <value>True if the payload is complete.</value>
        public bool IsReady {get; set;}

        /// <summary>
        /// The completed payload buffer
        /// </summary>
        /// <value>The payload buffer</value>
        public byte[] Payload {get; set;}

        /// <summary>
        /// Convenience property to generate an instance where IsReady = false
        /// </summary>
        /// <returns>A PacketAssemblyResult instance</returns>
        public static PacketAssemblyResult NotReady => new PacketAssemblyResult() { IsReady = false };

        /// <summary>
        /// A convenience method to generate an instance where IsReady = true and a payload is available
        /// </summary>
        /// <param name="payload">The payload to include in the result</param>
        /// <returns>A PacketAssemblyResult instance</returns>
        public static PacketAssemblyResult Ready(byte[] payload) {
            return (new PacketAssemblyResult()
                {
                    IsReady = true,
                    Payload = payload
                }
            );
        }
    }

    /// <summary>
    /// This helper class is used to identify and reassemble fragmented TCP payloads.
    /// </summary>
    public class PacketAssembler
    {
        private ILogger<PacketAssembler> logger;
        private int expectedLen;
        private byte[] payload;

        public PacketAssembler(ILogger<PacketAssembler> logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// Processes the current packet. If this packet is part of a fragmented sequence,
        /// its payload will be added to the internal buffer until the entire sequence payload has
        /// been loaded. Check <c>PacketAssemblyResult.Status</c> to know if the sequence is ready to use or not.
        /// </summary>
        /// <param name="packet">The packet to process</param>
        /// <returns>A <c>PacketAssemblyResult</c> that determines whether the packet has been fully assembled or not</returns>
        public PacketAssemblyResult Assemble(TcpPacket packet)
        {
            var result = new PacketAssemblyResult();

            // New packet sequence
            if (payload == null) 
            {
                payload = packet.PayloadData;

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
                if (packet.Push)
                {
                    logger.LogDebug($"Complete packet - Expected: {expectedLen}, Actual: {payload.Length}, Push: {packet.Push}");

                    result = PacketAssemblyResult.Ready(payload.ToArray());

                    // clear out state for the next sequence
                    Reset();
                }
                // the payload will be spread out across multiple packets
                else
                {
                    logger.LogDebug($"Fragmented packet detected - Expected: {expectedLen}, Actual: {payload.Length}, Push: {packet.Push}");

                    result = PacketAssemblyResult.NotReady;
                }
            }
            // reconstructing a fragmented sequence
            else
            {
                // Append this packet's payload to the fragment one we're currently reassembling
                payload = payload.Concat(packet.PayloadData).ToArray();

                // SOMETIMES we get a PSH flag even though we haven't gotten all of the packets we expect to
                if (packet.Push && (payload.Length >= expectedLen))
                {
                    // This should be the last packet in this sequence so grab everthing that's expected
                    payload = payload.Take(expectedLen).ToArray();
                    logger.LogDebug($"Fragmented packet completed!, Expected: {expectedLen}, Actual: {payload.Length}, Packet: {packet.PayloadData.Length}, Push: {packet.Push}");

                    result = PacketAssemblyResult.Ready(payload.ToArray());
                        
                    // wait... one more thing
                    if (payload.Length > expectedLen)
                    {
                        logger.LogWarning($"OVERFLOW bytes detected - Len: {payload.Length - expectedLen}, PayloadData: {BitConverter.ToString(payload.Skip(expectedLen).ToArray()).Replace("-", "")}\n\r");

                        // this is temporary until I decide what to do with these bytes
                        Reset();
                    }
                    else 
                    {
                        // clear out state for the next sequence
                        Reset();
                    }
                }
                else
                {
                    // This is an intermediate packet in the sequeunce
                    logger.LogDebug($"Combining packets - Expected: {expectedLen}, Actual: {payload.Length}, Packet: {packet.PayloadData.Length}, Push: {packet.Push}");
                
                    // need to wait for more packets
                    result = PacketAssemblyResult.NotReady;
                }
            }

            return (result);
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