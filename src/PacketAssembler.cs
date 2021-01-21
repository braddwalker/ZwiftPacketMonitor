using System;
using System.Linq;
using PacketDotNet;
using Microsoft.Extensions.Logging;


namespace ZwiftPacketMonitor
{
    public enum PacketAssemblyStatus
    {
        /// <summary>
        /// The packet assembly is complete and ready for consumption
        /// </summary>
        Ready,

        /// <summary>
        /// The packet assembly is not complete
        /// </summary>
        NotReady,
    }

    public class PacketAssemblyResult
    {
        public PacketAssemblyStatus Status {get; set;}
        public byte[] Payload {get; set;}

        public static PacketAssemblyResult NotReady => new PacketAssemblyResult() { Status = PacketAssemblyStatus.NotReady};
    }

    /// <summary>
    /// This helper class is used to reassembly fragmented TCP payloads.
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
        /// been loaded.
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

                    result.Status = PacketAssemblyStatus.Ready;
                    result.Payload = payload.ToArray();

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
                // Append this packet's payload to the larger fragmented one we're currently reassembling
                payload = payload.Concat(packet.PayloadData).ToArray();

                // SOMETIMES we get a PSH flag even though we haven't gotten all of the packets we expect to
                if (packet.Push && (payload.Length >= expectedLen))
                {
                    // This should be the last packet in this sequence so grab everthing that's expected
                    payload = payload.Take(expectedLen).ToArray();
                    logger.LogDebug($"Fragmented packet completed!, Expected: {expectedLen}, Actual: {payload.Length}, Packet: {packet.PayloadData.Length}, Push: {packet.Push}");

                    result.Status = PacketAssemblyStatus.Ready;
                    result.Payload = payload.ToArray();

                    // clear out state for the next sequence
                    Reset();

                    /*
                    if (fragmentedBytes.Length > fragmentedPayloadLength)
                    {
                        // This is technically the next game packet, so we'll need to treat it as if we received it as the first packet in another sequence
                        fragmentedBytes = fragmentedBytes.Skip(fragmentedPayloadLength).ToArray();

                        var payloadLenBytes = packetBytes.Take(2).ToArray();
                        if (BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(payloadLenBytes);
                        }

                        // first 2 bytes are the total payload length
                        fragmentedPayloadLength = BitConverter.ToUInt16(payloadLenBytes, 0);

                        logger.LogDebug($"OVERFLOW: Expected: {fragmentedPayloadLength}, Actual: {fragmentedBytes.Length}, PayloadData: {BitConverter.ToString(fragmentedBytes).Replace("-", "")}\n\r");
                    }
                    else 
                    {
                        // Reset these for the next sequence
                        fragmentedBytes = null;
                        fragmentedPayloadLength = 0;
                    }
                    */
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