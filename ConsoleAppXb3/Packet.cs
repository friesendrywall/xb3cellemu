using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleAppXb3
{
    class Packet
    {
        public enum API_CMD {
            API_OUT_ATCMD = 0x08,
            API_OUT_ATCMD_QUEUE = 0x09,
            API_OUT_SMS = 0x1F,
            API_OUT_IPV4_REQ = 0x20,
            API_OUT_IPV4_TLS = 0x23,
            API_IN_ATCMD_RESP = 0x88,
            API_IN_TX_STATUS = 0x89,
            API_IN_MODEM_STATUS = 0x8A,
            API_IN_SMS_PKT = 0x9F,
            API_IN_IPV4_PKT = 0xB0,
            API_OUT_SOCKET_CREATE = 0x40,
            API_OUT_SOCKET_OPT_REQ = 0x41,
            API_OUT_SOCKET_CONNECT = 0x42,
            API_OUT_SOCKET_CLOSE = 0x43,
            API_OUT_SOCKET_SEND = 0x44,
            API_OUT_SOCKET_SEND_TO = 0x45,
            API_OUT_SOCKET_BIND = 0x46,
            API_IN_SOCKET_CREATE_RESP = 0xC0,
            API_IN_SOCKET_OPT_RESP = 0xC1,
            API_IN_SOCKET_CONNECT_RESP = 0xC2,
            API_IN_SOCKET_CLOSE_RESP = 0xC3,
            API_IN_SOCKET_LISTEN_RESP = 0xC6,
            API_IN_SOCKET_IPV4_CLIENT = 0xCC,
            API_IN_SOCKET_RX = 0xCD,
            API_IN_SOCKET_RX_FROM = 0xCE,
            API_IN_SOCKET_STATUS = 0xCF,
        };

        public class API_TX
        {
            byte[] data;
            byte FrameID;
            public API_TX(byte FrameID, byte[] data)
            {
                this.data = data;
                this.FrameID = FrameID;
            }

            public byte[] API()
            {
                byte checkSum = this.FrameID;
                byte[] ReturnValue = new byte[data.Length + 5];
                int APIlen = data.Length + 1;
                ReturnValue[0] = 0x7E;//Start delimiter
                ReturnValue[1] = (byte)(APIlen >> 8);//high byte length
                ReturnValue[2] = (byte)(APIlen & 0xFF);//low byte length
                ReturnValue[3] = this.FrameID;
                for (int i = 0; i < data.Length; i++)
                {
                    checkSum += data[i];
                    ReturnValue[4 + i] = data[i];
                }
                ReturnValue[4 + data.Length] = (byte)(0xFF - checkSum);
                return ReturnValue;
            }
        }
        public class API_RX
        {
            public byte[] data;
            public byte FrameID;
            public API_RX(byte frameId, byte[] buf, int len)
            {
                data = new byte[len];
                FrameID = frameId;
                Buffer.BlockCopy(buf, 0, data, 0, len);
            }
        }
        public enum PacketError { ByteLength, ID, Checksum }
        enum ReceiveState { Delimiter, LengthMSB, LengthLSB, FrameID, RX, Checksum }
        const int MAX_LENGTH = 1550;
        ReceiveState PacketState = ReceiveState.Delimiter;
        int Length;
        int len;
        int Checksum;
        byte FrameID;
        int buffIndex = 0;
        byte[] Buff = new byte[MAX_LENGTH + 1];
        public delegate void NewPacketHandler(Packet.API_RX NewPacket);
        public event NewPacketHandler NewPacketEvent;

        public delegate void PacketErrorHandler(PacketError Error);
        public event PacketErrorHandler PacketErrorEvent;

        private void Reset()
        {
            Length = 0;
            Checksum = 0;
            PacketState = ReceiveState.Delimiter;
            FrameID = 0;
            buffIndex = 0;
            /*Source_Address = 0;
            RSSI = -110;
            Options = 0;
            ByteCount = 0;
            if (RF_Data != null)
                RF_Data.Dispose();
            RF_Data = null;
            TXstatus = 0;*/
        }

        private void Parser(byte CurrentByte)
        {

            switch (PacketState)
            {
                case ReceiveState.Delimiter:
                    if (CurrentByte == 0x7E)
                    {
                        PacketState = ReceiveState.LengthMSB;
                    }
                    break;
                case ReceiveState.LengthMSB: Length = CurrentByte; 
                    PacketState = ReceiveState.LengthLSB;
                    break;
                case ReceiveState.LengthLSB:
                    Length <<= 8; Length += CurrentByte;
                    //Error checking
                    if (Length > MAX_LENGTH)
                    {
                        PacketErrorEvent(PacketError.ByteLength);
                        Reset(); 
                        return;
                    }
                    //Continue, is ok
                    len = Length;
                    PacketState = ReceiveState.FrameID;
                    break;
                case ReceiveState.FrameID:
                    len--;
                    FrameID = CurrentByte; 
                    PacketState = ReceiveState.RX;
                    Checksum += CurrentByte;
                    break;
                case ReceiveState.RX:
                    Buff[buffIndex++] = CurrentByte;
                    Checksum += CurrentByte;
                    len--;
                    if (len == 0)
                    {
                        PacketState = ReceiveState.Checksum;
                    }
                    else if (buffIndex > MAX_LENGTH)
                    {
                        PacketState = ReceiveState.Delimiter;
                    }
                    break;
                case ReceiveState.Checksum:
                    Checksum += CurrentByte;
                    Checksum &= 0xFF;
                    if (Checksum == 0xFF)//We need to load up a new packet
                    {
                        Packet.API_RX NewPacket = new API_RX(FrameID, Buff, Length - 1);
                        //Notify handler that we have a new packet, and hand it off
                        NewPacketEvent(NewPacket);
                    }
                    else
                    {
                        PacketErrorEvent(PacketError.Checksum);
                    }
                    Reset();
                    break;
            }

        }
        public void StreamInput(string SerialStream)
        {
            byte[] ByteArray = ASCIIEncoding.Default.GetBytes(SerialStream);
            for (int a = 0; a < ByteArray.Length; a++)
            {
                Parser(ByteArray[a]);//Send bytes to be parsed
            }
        }
    }
}
