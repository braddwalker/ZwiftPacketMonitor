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
        public void Packet_InvalidPayload()
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

        [TestMethod]
        public void Packet_CompleteSinglePacket()
        {
            var payloadReady = false;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                payloadReady = true;
            };
            
            var packet = new TcpPacket(0, 0);
            packet.Push = true;
            packet.Acknowledgment = true;
            packet.PayloadData = CreatePacketPayload(new byte[] {0x01});

            pa.Assemble(packet);

            Assert.IsTrue(payloadReady);
        }

        [TestMethod]
        public void Packet_CompleteSinglePacket_NoPush()
        {
            var payloadReady = false;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                payloadReady = true;
            };
            
            var packet = new TcpPacket(0, 0);
            packet.Push = false;
            packet.Acknowledgment = true;
            packet.PayloadData = CreatePacketPayload(new byte[] {0x01});

            pa.Assemble(packet);

            Assert.IsFalse(payloadReady);
        }

        [TestMethod]
        public void Packet_CompleteSinglePacket_NoAck()
        {
            var payloadReady = false;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                payloadReady = true;
            };
            
            var packet = new TcpPacket(0, 0);
            packet.Push = true;
            packet.Acknowledgment = false;
            packet.PayloadData = CreatePacketPayload(new byte[] {0x01});

            pa.Assemble(packet);

            Assert.IsFalse(payloadReady);
        }

        [TestMethod]
        public void Packet_FragmentedSinglePacket_Two()
        {
            byte[] assembledPayload = null;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                assembledPayload = e.Payload; 
            };
            
            var payload = CreatePacketPayload(new byte[] {0x01, 0x01});
            var packet = new TcpPacket(0, 0);
            packet.Push = false;
            packet.Acknowledgment = true;
            packet.PayloadData = payload.Take(payload.Length - 1).ToArray();
            pa.Assemble(packet);

            packet = new TcpPacket(0, 0);
            packet.Push = true;
            packet.Acknowledgment = true;
            packet.PayloadData = payload.Skip(payload.Length - 1).ToArray();
            pa.Assemble(packet);

            Assert.IsTrue(Enumerable.SequenceEqual(payload.Skip(2), assembledPayload));
        }

        [TestMethod]
        public void Packet_FragmentedSinglePacket_Three()
        {
            byte[] assembledPayload = null;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                assembledPayload = e.Payload; 
            };
            
            var payload = CreatePacketPayload(new byte[] {0x01, 0x01, 0x01});
            var packet = new TcpPacket(0, 0);
            packet.Push = false;
            packet.Acknowledgment = true;
            packet.PayloadData = payload.Take(3).ToArray();
            pa.Assemble(packet);

            packet = new TcpPacket(0, 0);
            packet.Push = false;
            packet.Acknowledgment = true;
            packet.PayloadData = payload.Skip(2).Take(1).ToArray();
            pa.Assemble(packet);

            packet = new TcpPacket(0, 0);
            packet.Push = true;
            packet.Acknowledgment = true;
            packet.PayloadData = payload.Skip(payload.Length - 1).ToArray();
            pa.Assemble(packet);

            Assert.IsTrue(Enumerable.SequenceEqual(payload.Skip(2), assembledPayload));
        }
    }
}