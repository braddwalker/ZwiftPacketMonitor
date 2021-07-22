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
    /// 
    /// Many thanks to @jeroni7100 for figuring out the packet reassembly magic!
    /// </summary>
    public class PacketAssembler
    {
        public event EventHandler<PayloadReadyEventArgs> PayloadReady;

        private ILogger<PacketAssembler> _logger;
        private int _assembledLen;
        private byte[] _payload;
        private bool _complete;

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
            packet = packet ?? throw new ArgumentException(nameof(packet));

            try
            {
                if (packet.Push && packet.Acknowledgment && _payload == null)
                {
                    // No reassembly required
                    _payload = packet.PayloadData;
                    _assembledLen = packet.PayloadData.Length;
                    _complete = true;

                    _logger.LogDebug($"Complete packet - Actual: {_payload.Length}, Push: {packet.Push}, Ack: {packet.Acknowledgment}");
                }
                else if (packet.Push && packet.Acknowledgment)
                {
                    // Last packet in the sequence
                    _payload = _payload.Concat(packet.PayloadData).ToArray();
                    _assembledLen += packet.PayloadData.Length;
                    _complete = true;

                    _logger.LogDebug($"Fragmented sequence finished - Actual: {_payload.Length}, Push: {packet.Push}, Ack: {packet.Acknowledgment}");
                }
                else if (packet.Acknowledgment && _payload == null)
                {
                    // First packet in a sequence
                    _payload = packet.PayloadData;
                    _assembledLen = packet.PayloadData.Length;

                    _logger.LogDebug($"Fragmented packet started - Actual: {_payload.Length}, Push: {packet.Push}, Ack: {packet.Acknowledgment}");
                }
                else if (packet.Acknowledgment) {
                    // Middle packet in a sequence
                    _payload = _payload.Concat(packet.PayloadData).ToArray();
                    _assembledLen += packet.PayloadData.Length;

                    _logger.LogDebug($"Fragmented packet continued - Actual: {_payload.Length}, Push: {packet.Push}, Ack: {packet.Acknowledgment}");
                }

                if (_complete && _payload?.Length > 0)
                {
                    _logger.LogDebug($"Packet completed!, Actual: {_assembledLen}, Push: {packet.Push}, Ack: {packet.Acknowledgment}");

                    // Break apart any concatenated payloads
                    var offset = 0;
                    var length = 0;

                    // No need to decode the payload if debug isn't enabled
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug($"FULL PAYLOAD: {BitConverter.ToString(_payload.ToArray()).Replace("-", "")}\n\r");
                    }

                    while (offset < _assembledLen)
                    {
                        length = ToUInt16(_payload, offset, 2);

                        if (offset + length < _assembledLen)
                        {
                            var payload = _payload.Skip(offset + 2).Take(length).ToArray();

                            if (payload.Length > 0)
                            {
                                OnPayloadReady(new PayloadReadyEventArgs() { Payload = payload });

                                // No need to decode the payload if debug isn't enabled
                                if (_logger.IsEnabled(LogLevel.Debug))
                                {
                                    _logger.LogDebug($"{BitConverter.ToString(payload.ToArray()).Replace("-", "")}\n\r");
                                }
                            }
                        }

                        offset += 2 + length;
                        length = 0;
                    }

                    Reset();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ERROR");
                Reset();
            }
        }

        /// <summary>
        /// Resets the internal state to start over
        /// </summary>
        public void Reset()
        {
            _payload = null;
            _assembledLen = 0;
            _complete = false;
        }

        private int ToUInt16(byte[] buffer, int start, int count)
        {
            if (buffer.Length > 2)
            {
                var b = buffer.Skip(start).Take(count).ToArray();
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(b);
                }

                return (BitConverter.ToUInt16(b, 0));
            }
            else 
            {
                return (0);
            }
        }
    }
}