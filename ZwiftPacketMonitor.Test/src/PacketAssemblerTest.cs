using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ZwiftPacketMonitor;

namespace ZwiftPacketMonitor.Test
{
    [TestClass]
    public class PacketAssemblerTest : BaseTest
    {
        [TestMethod]
        public void Test_Initialize_NoErrors()
        {
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());

        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Test_NullPacket_ArgumentException()
        {
            var pa = new PacketAssembler(CreateLogger<PacketAssembler>());
            pa.Assemble(null);
        }
    }
}