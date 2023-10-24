using System;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using System.Threading;
using System.Diagnostics;
using System.Net.Sockets;

namespace ConsoleAppXb3
{
    class Program
    {
        static SerialPort _serialPort;
        static Packet pkt = new Packet();
        static Queue packetQueue = new Queue();
        static byte[] rxBuff = new byte[2000];
        class sock
        {
            public sock()
            {
                sockType = type.tcp;
                sockState = state.waiting;
                allocated = false;
            }
            public enum type
            {
                udp = 0, tcp = 1
            }
            public enum state
            {
                waiting, opened, connected, closed
            }
            public type sockType;
            public state sockState;
            public bool allocated;
            public Socket socket;
        }

        static sock[] sockets = new sock[4];

        static int Main(string[] args)
        {
            int i;
            Console.WriteLine("{0} Opening {1}", DateTime.UtcNow.ToString("HH:mm:ss.fff"), args[1]);
            try
            {
                _serialPort = new SerialPort();
                _serialPort.PortName = args[0];
                _serialPort.BaudRate = Convert.ToInt32(args[1]);
                _serialPort.Parity = Parity.None;
                _serialPort.DataBits = 8;
                _serialPort.StopBits = StopBits.One;
                _serialPort.Handshake = Handshake.None;
                _serialPort.WriteTimeout = 5000;
                _serialPort.ParityReplace = 255;
                _serialPort.Encoding = System.Text.Encoding.Default;
                _serialPort.DataReceived += _serialPort_DataReceived;
                _serialPort.Open();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception {0}", ex.ToString());
                Console.WriteLine("Could not open port {0}", args[1]);
                return 1;
            }
            for (i = 0; i < sockets.Length; i++)
            {
                sockets[i] = new sock();
            }

            pkt.NewPacketEvent += Pkt_NewPacketEvent;
            pkt.PacketErrorEvent += Pkt_PacketErrorEvent;
            Thread.Sleep(1000);
            /* Send modem reset */
            Packet.API_TX tx = new Packet.API_TX((byte)Packet.API_CMD.API_IN_MODEM_STATUS, new byte[1] { 00 });
            _serialPort.Write(tx.API(), 0, tx.API().Length);
            Thread.Sleep(1000);
            /* Send modem asociated */
            tx = new Packet.API_TX((byte)Packet.API_CMD.API_IN_MODEM_STATUS, new byte[1] { 2/* ASOC */ });
            _serialPort.Write(tx.API(), 0, tx.API().Length);
            i = 0;
            for (; ; )
            {
                Thread.Sleep(10);
                /* Handle new packets */
                while (packetQueue.Count > 0)
                {
                    Packet.API_RX rx = (Packet.API_RX)packetQueue.Dequeue();
                    switch (rx.FrameID)
                    {
                        case (byte)Packet.API_CMD.API_OUT_ATCMD:
                            atCmdRequest(rx.data);
                            break;
                        case (byte)Packet.API_CMD.API_OUT_SOCKET_CREATE:
                            createSocket(rx.data);
                            break;
                        case (byte)Packet.API_CMD.API_OUT_SOCKET_CONNECT:
                            connectSocket(rx.data);
                            break;
                        case (byte)Packet.API_CMD.API_OUT_SOCKET_CLOSE:
                            closeSocket(rx.data);
                            break;
                        case (byte)Packet.API_CMD.API_OUT_SOCKET_SEND:
                            socketSend(rx.data);
                            break;
                        default:
                            Console.WriteLine("{0} RX {1:X} unhandled", DateTime.UtcNow.ToString("HH:mm:ss.fff"), rx.FrameID);
                            break;
                    }
                }
                /* Check for socket incoming */
                if (sockets[i].socket == null)
                {

                }
                else if (sockets[i].allocated && sockets[i].socket.Connected)
                {
                    try
                    {
                        int bytesReceived = sockets[i].socket.Receive(rxBuff, 1500, SocketFlags.None);
                        if (bytesReceived == 0)
                        {
                            socketWasClosed((byte)i);
                        }
                        else
                        {
                            socketReceive((byte)i, rxBuff, bytesReceived);
                        }
                    }
                    catch { }
                }
                else if (sockets[i].allocated && !sockets[i].socket.Connected && sockets[i].sockState == sock.state.connected)
                {
                    try
                    {
                        int bytesReceived = sockets[i].socket.Receive(rxBuff, 1500, SocketFlags.None);
                        if (bytesReceived == 0)
                        {
                            socketWasClosed((byte)i);
                        }
                        else
                        {
                            socketReceive((byte)i, rxBuff, bytesReceived);
                        }
                    }
                    catch
                    {
                        socketWasClosed((byte)i);
                    }


                }
                /* Increment */
                i++;
                if (i == sockets.Length)
                {
                    i = 0;
                }
            }
            return 0;
        }

        private static void socketWasClosed(byte socket)
        {
            sockets[socket].sockState = sock.state.closed;
            Packet.API_TX tx = new Packet.API_TX((byte)Packet.API_CMD.API_IN_SOCKET_STATUS, new byte[] { socket, 0x0C });
            _serialPort.Write(tx.API(), 0, tx.API().Length);
            Console.WriteLine("{0} sock[{1}] was closed", DateTime.UtcNow.ToString("HH:mm:ss.fff"), socket);
        }
        private static void socketReceive(byte socket, byte[] buff, int len)
        {
            byte[] payload = new byte[len + 3];
            payload[0] = 0;/* Frame ID not used */
            payload[1] = socket;/* Socket ID */
            payload[2] = 0;/* Status */
            Buffer.BlockCopy(buff, 0, payload, 3, len);
            Packet.API_TX tx = new Packet.API_TX((byte)Packet.API_CMD.API_IN_SOCKET_RX, payload);
            _serialPort.Write(tx.API(), 0, tx.API().Length);
            Console.WriteLine("{0} sock[{1}] RX {2}", DateTime.UtcNow.ToString("HH:mm:ss.fff"), socket, len);
        }
        private static void socketSend(byte[] pkt)
        {
            string frameId = Encoding.ASCII.GetString(pkt, 0, 1);
            byte SocketID = pkt[1];
            byte txopt = pkt[2];
            string response = frameId;
            try
            {
                byte[] payload = new byte[pkt.Length - 3];
                Buffer.BlockCopy(pkt, 3, payload, 0, pkt.Length - 3);
                sock s = sockets[SocketID];
                // SEND HERE
                int sent = s.socket.Send(payload, payload.Length, SocketFlags.None);
                if (sent == payload.Length)
                {
                    response += Encoding.ASCII.GetString(new byte[1] { 0 });
                }
                else
                {
                    Console.WriteLine("{0} sock[{1}] tx err", DateTime.UtcNow.ToString("HH:mm:ss.fff"), SocketID);
                    response += Encoding.ASCII.GetString(new byte[1] { 0x21 });
                }
            }
            catch
            {
                // byte[] val = new byte[2] { /* Frame ID */pkt[0], 0x21 };
                response += Encoding.ASCII.GetString(new byte[1] { 0x21 });
            }
            Packet.API_TX tx = new Packet.API_TX((byte)Packet.API_CMD.API_IN_TX_STATUS,
                ASCIIEncoding.Default.GetBytes(response));
            _serialPort.Write(tx.API(), 0, tx.API().Length);
            Console.WriteLine("{0} sock[{1}] TX {2}", DateTime.UtcNow.ToString("HH:mm:ss.fff"), SocketID, pkt.Length - 3);
        }
        private static void closeSocket(byte[] pkt)
        {
            string frameId = Encoding.ASCII.GetString(pkt, 0, 1);
            byte SocketID = pkt[1];
            string response = frameId;
            sock s = sockets[SocketID];
            try
            {
                s.socket.Shutdown(SocketShutdown.Both);
                s.socket.Close();
                byte[] val = new byte[2] { SocketID, 0 };
                response += Encoding.ASCII.GetString(val);
                s.sockState = sock.state.closed ;
            }
            catch
            {
                byte[] val = new byte[2] { SocketID, 0x20 };
                response += Encoding.ASCII.GetString(val);
            }
            s.allocated = false;
            Packet.API_TX tx = new Packet.API_TX((byte)Packet.API_CMD.API_IN_SOCKET_CLOSE_RESP,
                ASCIIEncoding.Default.GetBytes(response));
            _serialPort.Write(tx.API(), 0, tx.API().Length);
            Console.WriteLine("{0} sock[{1}] Closed", DateTime.UtcNow.ToString("HH:mm:ss.fff"), SocketID);
        }
        private static void connectSocket(byte[] pkt)
        {
            /* RX
             Byte Frame ID
             Byte Socket ID
             16 big end Dest port
             Byte Address type 0 = IPV4 1 = string
             Byte[] Dest address
             */
            string frameId = Encoding.ASCII.GetString(pkt, 0, 1);
            byte SocketID = pkt[1];
            ushort destPort = (ushort)((pkt[2] << 8) + pkt[3]);
            byte addrType = pkt[4];
            string destAddr = Encoding.ASCII.GetString(pkt, 5, pkt.Length - 5);
            string response = frameId;
            sock s = sockets[SocketID];
            try
            {
                s.socket.Connect(destAddr, destPort);
                s.socket.ReceiveTimeout = 1;
                byte[] val = new byte[2] { SocketID, 0 };
                response += Encoding.ASCII.GetString(val);
                s.sockState = sock.state.connected;
            }
            catch (SocketException sockEx)
            {
                byte[] val = new byte[2] { SocketID, 1 };
                response += Encoding.ASCII.GetString(val);
            }
            catch
            {
                byte[] val = new byte[2] { SocketID, 5 };
                response += Encoding.ASCII.GetString(val);
            }
            Packet.API_TX tx = new Packet.API_TX((byte)Packet.API_CMD.API_IN_SOCKET_CONNECT_RESP,
                ASCIIEncoding.Default.GetBytes(response));
            _serialPort.Write(tx.API(), 0, tx.API().Length);
            Console.WriteLine("{0} sock[{1}] Connecting", DateTime.UtcNow.ToString("HH:mm:ss.fff"), SocketID);
            if (s.socket.Connected)
            {
                Packet.API_TX stx = new Packet.API_TX((byte)Packet.API_CMD.API_IN_SOCKET_STATUS, new byte[] { SocketID, 0x00 });
                _serialPort.Write(stx.API(), 0, stx.API().Length);
                Console.WriteLine("{0} sock[{1}] Connected", DateTime.UtcNow.ToString("HH:mm:ss.fff"), SocketID);
            }
        }
        private static void createSocket(byte[] pkt)
        {
            /*
             FrameID Byte
             Protocol Byte 0 = UDP, 1 = TCP, 4 = SSL
             */
            string frameId = Encoding.ASCII.GetString(pkt, 0, 1);
            string response = frameId;
            sock selected = null;
            int i;
            for (i = 0; i < sockets.Length; i++)
            {
                if (!sockets[i].allocated)
                {
                    selected = sockets[i];
                    selected.allocated = true;
                    selected.sockState = sock.state.opened;
                    break;
                }
            }
            if (selected == null)
            {
                response += "\xFF\x32";
            }
            else
            {
                byte[] val = new byte[2] { (byte)i, 0 };
                response += Encoding.ASCII.GetString(val);
                selected.socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            }
            Packet.API_TX tx = new Packet.API_TX((byte)Packet.API_CMD.API_IN_SOCKET_CREATE_RESP,
                ASCIIEncoding.Default.GetBytes(response));
            _serialPort.Write(tx.API(), 0, tx.API().Length);
            Console.WriteLine("{0} sock[{1}] Created", DateTime.UtcNow.ToString("HH:mm:ss.fff"), i);
        }

        private static void actLikeRestart() {
            for (int i = 0; i < sockets.Length; i++)
            {
                sockets[i].allocated = false;
                sockets[i].socket = null;
            }
            Thread.Sleep(1500);
            /* Send modem reset */
            Console.WriteLine("{0} Sending Restart", DateTime.UtcNow.ToString("HH:mm:ss.fff"));
            Packet.API_TX tx = new Packet.API_TX((byte)Packet.API_CMD.API_IN_MODEM_STATUS, new byte[1] { 00 });
            _serialPort.Write(tx.API(), 0, tx.API().Length);
            Thread.Sleep(1000);
            /* Send modem asociated */
            tx = new Packet.API_TX((byte)Packet.API_CMD.API_IN_MODEM_STATUS, new byte[1] { 2/* ASOC */ });
            _serialPort.Write(tx.API(), 0, tx.API().Length);
        }
        private static void atCmdRequest(byte[] pkt)
        {
            string cmd = "";
            string response = "";
            string frameId = Encoding.ASCII.GetString(pkt, 0, 1);
            cmd += Encoding.ASCII.GetString(pkt, 1, 2);
            Console.WriteLine("{0} RX AT {1}", DateTime.UtcNow.ToString("HH:mm:ss.fff"), cmd);
            switch (cmd)
            {
                case "00":
                    actLikeRestart();
                    return;
                case "PH":
                    response += frameId;
                    response += "PH\x0+16201112222";
                    // TODO close open sockets
                    break;
                case "MV":
                    response += frameId;
                    response += "MV\x0MVEMULATOR_1.0";
                    break;
                case "DT":
                    /* 2023-10-16T17:05:16-04:00 */
                    response += frameId + "DT\x0";
                    response += DateTime.UtcNow.ToString("s");
                    break;
                case "DB":
                    response += frameId;
                    response += "DB\x0\x046";
                    break;
                case "SQ":
                    response += frameId;
                    response += "SQ\x0\x0A\x0A";
                    break;
                case "SW":
                    response += frameId;
                    response += "SW\x0\x0A\x0A";
                    break;
                case "MY":
                    response += frameId;
                    response += "MY\x0\xC0\xA8\x0\x7B";
                    break;
            }
            if (response != "")
            {
                Packet.API_TX tx = new Packet.API_TX((byte)Packet.API_CMD.API_IN_ATCMD_RESP,
                        ASCIIEncoding.Default.GetBytes(response));
                _serialPort.Write(tx.API(), 0, tx.API().Length);
                Console.WriteLine("{0} TX AT {1}", DateTime.UtcNow.ToString("HH:mm:ss.fff"), cmd);
            }
        }

        private static void Pkt_PacketErrorEvent(Packet.PacketError Error)
        {
            Console.WriteLine("Packet error " + Error.ToString() + "\r\n");
        }

        private static void Pkt_NewPacketEvent(Packet.API_RX NewPacket)
        {
            // Debug.WriteLine("Packet rx {0}" + NewPacket.data.Length);
            packetQueue.Enqueue(NewPacket);
        }

        static void _serialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            string buffer = ((SerialPort)sender).ReadExisting();
            pkt.StreamInput(buffer);
        }
    }
}
