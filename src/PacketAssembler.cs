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

        private ILogger<PacketAssembler> _logger;
        private int _expectedLen;
        private byte[] _payload;

        public PacketAssembler(ILogger<PacketAssembler> logger)
        {
            _logger = logger ?? throw new ArgumentException(nameof(logger));
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
            if (_payload == null) 
            {
                _payload = buffer;

                if (_payload.Length > 2)
                {
                    // first 2 bytes are the total payload length
                    var payloadLenBytes = _payload.Take(2).ToArray();
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(payloadLenBytes);
                    }

                    _expectedLen = BitConverter.ToUInt16(payloadLenBytes, 0);
                }

                // trim off the header
                _payload = _payload.Skip(2).ToArray();

                if (_payload.Length >= _expectedLen)
                {
                    // Any bytes past the expectedLen are overflow from the next message
                    var overflow = _payload.Skip(_expectedLen).ToArray();
                    _logger.LogDebug($"Complete packet - Expected: {_expectedLen}, Actual: {_payload.Length}, Push: {packet.Push}");

                    OnPayloadReady(new PayloadReadyEventArgs() { Payload = _payload.ToArray() });

                    // See if the next packet sequence is comingled with this one
                    if (overflow.Length > 0)
                    {
                        _logger.LogWarning($"OVERFLOW bytes detected - Len: {overflow.Length}, PayloadData: {BitConverter.ToString(overflow.ToArray()).Replace("-", "")}\n\r");
                        
                        // Start the process over as if this overflow data came in fresh from a packet
                        AssembleInternal(packet, overflow);
                    }
                }
                // the payload will be spread out across multiple packets
                else
                {
                    _logger.LogDebug($"Fragmented packet detected - Expected: {_expectedLen}, Actual: {_payload.Length}, Push: {packet.Push}");
                }
            }
            // reconstructing a fragmented sequence
            else
            {
                // Append this packet's payload to the fragment one we're currently reassembling
                _logger.LogDebug($"Combining packets - Expected: {_expectedLen}, Actual: {_payload.Length}, Packet: {packet.PayloadData.Length}, Push: {packet.Push}");
                _payload = _payload.Concat(packet.PayloadData).ToArray();

                if (packet.Push)
                {
                    _logger.LogDebug($"Fragmented packet completed!, Expected: {_expectedLen}, Actual: {_payload.Length}, Packet: {packet.PayloadData.Length}, Push: {packet.Push}");

                    // our original fragmented packet is ready to ship
                    OnPayloadReady(new PayloadReadyEventArgs() { Payload = _payload.Take(_expectedLen).ToArray() });
                }

                /*
                if (_payload.Length >= _expectedLen)
                {
                    // Any bytes past the expectedLen are overflow from the next message
                    var overflow = _payload.Skip(_expectedLen).ToArray();
                    _logger.LogDebug($"Fragmented packet completed!, Expected: {_expectedLen}, Actual: {_payload.Length}, Packet: {packet.PayloadData.Length}, Push: {packet.Push}");

                    // our original fragmented packet is ready to ship
                    OnPayloadReady(new PayloadReadyEventArgs() { Payload = _payload.Take(_expectedLen).ToArray() });

                    // See if the next packet sequence is comingled with this one
                    if (overflow.Length > 0)
                    {
                        _logger.LogDebug($"OVERFLOW bytes detected - Len: {overflow.Length}, PayloadData: {BitConverter.ToString(overflow.ToArray()).Replace("-", "")}\n\r");
                        
                        // Start the process over as if this overflow data came in fresh from a packet
                        AssembleInternal(packet, overflow);
                    }
                }
                */
            }
        }

        /// <summary>
        /// Resets the internal state to start over
        /// </summary>
        public void Reset()
        {
            _payload = null;
            _expectedLen = 0;
        }
    }
}