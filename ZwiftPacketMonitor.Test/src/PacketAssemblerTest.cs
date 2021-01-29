using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PacketDotNet;
using System.Linq;

namespace ZwiftPacketMonitor.Test
{    
    [TestClass]
    public class PacketAssemblerTest : BaseTest
    {
        private byte[] CreatePacketPayload(byte[] payload, int headerIndex = 0)
        {
            return (BitConverter.GetBytes((Int16)payload.Length)
                .Reverse()
                .Concat(PacketAssembler.HEADERS[headerIndex])
                .Concat(payload)
                .ToArray());
        }

        private byte[] CreatePacketPayloadWithHeader(int length) 
        {
            var payload = Enumerable.Repeat((byte)0x01, length - PacketAssembler.HEADERS[0].Length).ToArray();
            return (BitConverter.GetBytes((Int16)length)
                .Reverse()
                .Concat(PacketAssembler.HEADERS[0])
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

        [TestMethod]
        public void Packet_CompletePayload_InvalidHeader()
        {
            var payloadReady = false;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                payloadReady = true;
            };

            var packet = new TcpPacket(0, 0);
            packet.PayloadData = new byte[] {0x0, 0x01, 0x0};

            pa.Assemble(packet);

            Assert.IsFalse(payloadReady);
        }

        [TestMethod]
        public void Packet_CompletePayload_ValidHeader1()
        {
            var payloadReady = false;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                payloadReady = true;
            };

            var packet = new TcpPacket(0, 0);
            packet.PayloadData = CreatePacketPayload(new byte[] {0x01}, 0);

            pa.Assemble(packet);

            Assert.IsTrue(payloadReady);
        }

        [TestMethod]
        public void Packet_CompletePayload_ValidHeader2()
        {
            var payloadReady = false;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                payloadReady = true;
            };

            var packet = new TcpPacket(0, 0);
            packet.PayloadData = CreatePacketPayload(new byte[] {0x01}, 1);

            pa.Assemble(packet);

            Assert.IsTrue(payloadReady);
        }

        [TestMethod]
        public void Packet_FragmentedPayload_IncompleteSequence()
        {
            var payloadReady = false;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                payloadReady = true;
            };

            var payload = CreatePacketPayloadWithHeader(10);

            var packet = new TcpPacket(0, 0);
            packet.PayloadData = payload.Take(7).ToArray();
            pa.Assemble(packet);

            packet.PayloadData = payload.Skip(7).Take(1).ToArray();
            pa.Assemble(packet);

            Assert.IsFalse(payloadReady);
        }

        [TestMethod]
        public void Packet_FragmentedPayload_CompleteSequence()
        {
            var payloadReady = false;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                payloadReady = true;
            };

            var payload = CreatePacketPayloadWithHeader(10);

            var packet = new TcpPacket(0, 0);
            packet.PayloadData = payload.Take(7).ToArray();
            pa.Assemble(packet);

            packet.PayloadData = payload.Skip(7).ToArray();
            pa.Assemble(packet);

            Assert.IsTrue(payloadReady);
        }

        [TestMethod]
        public void Packet_FragmentedPayload_CompleteSequence_3Fragments()
        {
            var payloadReady = false;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                payloadReady = true;
            };

            var payload = CreatePacketPayloadWithHeader(10);

            var packet = new TcpPacket(0, 0);
            packet.PayloadData = payload.Take(7).ToArray();
            pa.Assemble(packet);

            packet.PayloadData = payload.Skip(7).Take(1).ToArray();
            pa.Assemble(packet);

            packet.PayloadData = payload.Skip(8).ToArray();
            pa.Assemble(packet);

            Assert.IsTrue(payloadReady);
        }

        [TestMethod]
        public void Packet_Complete_Verify()
        {
            var buffer = new byte[0];
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                buffer = e.Payload;
            };

            var payload = CreatePacketPayloadWithHeader(10);

            var packet = new TcpPacket(0, 0);
            packet.PayloadData = payload.ToArray();
            pa.Assemble(packet);

            CollectionAssert.AreEqual(payload.Skip(2).ToArray(), buffer);
        }

        [TestMethod]
        public void Packet_Fragmented_Incomplete_Verify()
        {
            var buffer = new byte[0];
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                buffer = e.Payload;
            };

            var payload = CreatePacketPayloadWithHeader(10);

            var packet = new TcpPacket(0, 0);
            packet.PayloadData = payload.Take(8).ToArray();
            pa.Assemble(packet);

            packet.PayloadData = payload.Skip(8).Take(1).ToArray();
            pa.Assemble(packet);

            CollectionAssert.AreEqual(new byte[0], buffer);
        }

        [TestMethod]
        public void Packet_Fragmented_Complete_Verify()
        {
            var buffer = new byte[0];
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                buffer = e.Payload;
            };

            var payload = CreatePacketPayloadWithHeader(10);

            var packet = new TcpPacket(0, 0);
            packet.PayloadData = payload.Take(8).ToArray();
            pa.Assemble(packet);

            packet.PayloadData = payload.Skip(8).ToArray();
            pa.Assemble(packet);

            CollectionAssert.AreEqual(payload.Skip(2).ToArray(), buffer);
        }

        [TestMethod]
        public void Packet_Complete_With_Overflow_Verify()
        {
            byte[] buffer1 = null;
            byte[] buffer2 = null;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                if (buffer1 == null)
                    buffer1 = e.Payload;
                else
                    buffer2 = e.Payload;
            };

            var payload = CreatePacketPayloadWithHeader(10);
            var payload2 = CreatePacketPayloadWithHeader(10);
            var combined = payload.Concat(payload2).ToArray();

            var packet = new TcpPacket(0, 0);
            packet.PayloadData = combined;
            pa.Assemble(packet);

            CollectionAssert.AreEqual(payload.Skip(2).ToArray(), buffer1);
            CollectionAssert.AreEqual(payload2.Skip(2).ToArray(), buffer2);
        }

        [TestMethod]
        public void Packet_Complete_With_Invalid_Overflow_Verify()
        {
            byte[] buffer1 = null;
            byte[] buffer2 = null;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                if (buffer1 == null)
                    buffer1 = e.Payload;
                else
                    buffer2 = e.Payload;
            };

            var payload = CreatePacketPayloadWithHeader(10);
            var payload2 = CreatePacketPayloadWithHeader(10);
            var combined = payload.Concat(payload2.Skip(3)).ToArray();

            var packet = new TcpPacket(0, 0);
            packet.PayloadData = combined;
            pa.Assemble(packet);

            CollectionAssert.AreEqual(payload.Skip(2).ToArray(), buffer1);
            Assert.IsNull(buffer2);
        }

        [TestMethod]
        public void Packet_Fragmented_With_Overflow_Verify()
        {
            byte[] buffer1 = null;
            byte[] buffer2 = null;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                if (buffer1 == null)
                    buffer1 = e.Payload;
                else
                    buffer2 = e.Payload;
            };

            var payload = CreatePacketPayloadWithHeader(10);
            var payload2 = CreatePacketPayloadWithHeader(10);
            var combined = payload.Concat(payload2).ToArray();

            var packet = new TcpPacket(0, 0);
            packet.PayloadData = combined.Take(10).ToArray();
            pa.Assemble(packet);

            packet.PayloadData = combined.Skip(10).ToArray();
            pa.Assemble(packet);

            CollectionAssert.AreEqual(payload.Skip(2).ToArray(), buffer1);
            CollectionAssert.AreEqual(payload2.Skip(2).ToArray(), buffer2);
        }

        [TestMethod]
        public void Event_Verify_Dont_Bubble_Exception()
        {
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                throw new NullReferenceException();
            };

            var packet = new TcpPacket(0, 0);
            packet.PayloadData = CreatePacketPayloadWithHeader(10);
            pa.Assemble(packet);
        }

        [TestMethod]
        public void Packet_Reset()
        {
            byte[] buffer = null;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                buffer = e.Payload;
            };

            var payload = CreatePacketPayloadWithHeader(10);

            var packet = new TcpPacket(0, 0);
            packet.PayloadData = payload.Take(8).ToArray();
            pa.Assemble(packet);

            pa.Reset();

            packet.PayloadData = payload.Skip(8).ToArray();
            pa.Assemble(packet);

            Assert.IsNull(buffer);
        }

       [TestMethod]
        public void Packet_Reset_Then_Complete_Verify()
        {
            byte[] buffer = null;
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.PayloadReady += (s, e) =>
            {
                buffer = e.Payload;
            };

            var payload = CreatePacketPayloadWithHeader(10);

            var packet = new TcpPacket(0, 0);
            packet.PayloadData = payload.Take(8).ToArray();
            pa.Assemble(packet);

            pa.Reset();

            packet.PayloadData = payload.ToArray();
            pa.Assemble(packet);

            CollectionAssert.AreEqual(payload.Skip(2).ToArray(), buffer);
        }
    }
}