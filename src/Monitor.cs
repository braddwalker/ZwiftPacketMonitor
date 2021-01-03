using System;
using System.Linq;
using SharpPcap;
using System.Threading;
using System.Threading.Tasks;

namespace ZwiftPacketMonitor
{
    ///<summary>
    ///</summary>
    ///<author>Brad Walker - https://github.com/braddwalker</author>
    public class Monitor 
    {
        /// <summary>
        /// The default Zwift UDP data port
        /// </summary>
        private const int ZWIFT_PORT = 3022;

        /// <summary>
        /// Default read timeout for packet capture
        /// </summary>
        private const int READ_TIMEOUT = 1000;

        private string networkInterface;
        private ICaptureDevice device;

        /// <summary>
        /// This event gets invoked when player update events are received from the central Zwift game engine
        /// </summary>
        public event EventHandler<PlayerStateEventArgs> IncomingPlayerEvent;

        /// <summary>
        /// This event gets invoked when the local player updates get replicated to the central Zwift game engine
        /// </summary>
        public event EventHandler<PlayerStateEventArgs> OutgoingPlayerEvent;

        /// <summary>
        /// Creates a new instance of the <c>Monitor</c>.
        /// </summary>
        /// <param name="networkInterface">The network interface to attach to</param>
        public Monitor(string networkInterface) {
            this.networkInterface = networkInterface;
        }

        private void OnIncomingPlayerEvent(PlayerStateEventArgs e)
        {
            EventHandler<PlayerStateEventArgs> handler = IncomingPlayerEvent;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        private void OnOutgoingPlayerEvent(PlayerStateEventArgs e)
        {
            EventHandler<PlayerStateEventArgs> handler = OutgoingPlayerEvent;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        /// <summary>
        /// Starts the network monitor and begins dispatching events
        /// </summary>
        /// <param name="cancellationToken">An optional cancellation token</param>
        /// <returns>A Task representing the operation</returns>
        public async Task StartCaptureAsync(CancellationToken cancellationToken = default)
        {
            /* Retrieve the device list */
            var devices = CaptureDeviceList.Instance;
            device = devices.Where(x => x.Name.Equals(networkInterface)).FirstOrDefault();

            if (device == null)
            {
                throw new ArgumentException($"Interface {networkInterface} not found");
            }

            //Register our handler function to the 'packet arrival' event
            device.OnPacketArrival += new PacketArrivalEventHandler(device_OnPacketArrival);

            // Open the device for capturing
            device.Open(DeviceMode.Promiscuous, READ_TIMEOUT);
            device.Filter = $"udp port {ZWIFT_PORT}";

            // Start capture 'INFINTE' number of packets
            await Task.Run(() => { device.Capture(); }, cancellationToken);
        }

        /// <summary>
        /// Stops any active capture
        /// </summary>
        /// <param name="cancellationToken">An optional cancellation token</param>
        /// <returns>A Task representing the operation</returns>
        public async Task StopCaptureAsync(CancellationToken cancellationToken = default)
        {
            if (device == null)
            {
                await Task.CompletedTask;
            }
            else {
                await Task.Run(() => { device.Close(); }, cancellationToken);
            }
        }

        private void device_OnPacketArrival(object sender, CaptureEventArgs e)
        {
            try 
            {
                var packet = PacketDotNet.Packet.ParsePacket(e.Packet.LinkLayerType, e.Packet.Data);
                var udpPacket = packet.Extract<PacketDotNet.UdpPacket>();
                if (udpPacket != null)
                {
                    var ipPacket = (PacketDotNet.IPPacket)udpPacket.ParentPacket;

                    System.Net.IPAddress srcIp = ipPacket.SourceAddress;
                    System.Net.IPAddress dstIp = ipPacket.DestinationAddress;
                    int srcPort = udpPacket.SourcePort;
                    int dstPort = udpPacket.DestinationPort;

                    try 
                    {
                        if (srcPort == ZWIFT_PORT)
                        {
                            var packetData = ServerToClient.Parser.ParseFrom(udpPacket.PayloadData);
                            foreach (var player in packetData.PlayerStates)
                            {
                                PlayerStateEventArgs args = new PlayerStateEventArgs();
                                args.PlayerState = player;
                                args.EventDate = DateTime.Now;
                                OnIncomingPlayerEvent(args);
                            }

                            if (packetData.Msgnum == packetData.NumMsgs)
                            {
                                //System.Console.WriteLine("End of batch");
                            }
                        }
                        else if (dstPort == ZWIFT_PORT) 
                        {
                            var packetData = ClientToServer.Parser.ParseFrom(udpPacket.PayloadData);

                            PlayerStateEventArgs args = new PlayerStateEventArgs();
                            args.PlayerState = packetData.State;
                            args.EventDate = DateTime.Now;
                            OnOutgoingPlayerEvent(args);
                        }
                    }
                    catch (Exception ex) {
                        System.Console.WriteLine($"ERROR: PayloadLen: {udpPacket?.PayloadData?.Length}, SrcPort: {srcPort}, DestPort: {dstPort}, Error: {ex.GetType()}, {ex.Message}");
                    }
                }
            }
            catch (Exception ee)
            {
                System.Console.WriteLine($"Unable to parse packet - {ee.Message}");
            }
        }
    }
}