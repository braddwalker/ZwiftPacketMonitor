using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PacketDotNet;
using System.Linq;

namespace ZwiftPacketMonitor.Test
{    
    [TestClass]
    public class PacketAssemblerTest : BaseTest
    {
        private byte[] CreatePacketPayload(byte[] payload)
        {
            return (BitConverter.GetBytes((Int16)payload.Length)
                .Reverse()
                .Concat(payload)
                .ToArray());
        }

        [TestMethod]
        public void Initialize_NoErrors()
        {
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void NullPacket_ArgumentException()
        {
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.Assemble(null);
        }

        [TestMethod]
        public void Packet_NullPayload()
        {
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            var packet = new TcpPacket(0, 0);

            pa.Assemble(packet);
        }

        [TestMethod]
        public void Packet_InsufficientPayload()
        {
            var payloadReady = false;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                payloadReady = true;
            };
            
            var packet = new TcpPacket(0, 0);
            packet.PayloadData = new byte[] {0x01};

            pa.Assemble(packet);

            Assert.IsFalse(payloadReady);
        }
    }
}