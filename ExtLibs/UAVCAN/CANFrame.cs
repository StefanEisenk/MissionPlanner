using System;

namespace UAVCAN
{
    /// <summary>
    /// https://uavcan.org/Specification/4._CAN_bus_transport_layer/
    /// </summary>
    public class CANFrame
    {
        private byte[] packet_data;

        public CANFrame(byte[] packet_data)
        {
            this.packet_data = packet_data;
        }

        // message frame
        //0-127
        public byte SourceNode
        {
            get { return (byte)(packet_data[0] & 0x7f); }
        }
        public bool isServiceMsg
        {
            get { return (packet_data[0] & 0x80) > 0; }
        }
        // 0 - 65535    anon 0-3
        public UInt16 MsgTypeID
        {
            get { return BitConverter.ToUInt16(packet_data, 1); }
        }
        // 0-31 high-low
        public byte Priority
        {
            get { return (byte)(packet_data[3] & 0x1f); }
        }

        // anon frame
        public UInt16 Discriminator {
            get { return BitConverter.ToUInt16(packet_data, 1); }
        }

        // service frame
        //0-127
        public byte DestinationNode { get { return (byte)(packet_data[1] & 0x7f); } }
        public bool isRequest { get { return (packet_data[1] & 0x80) > 0; } }
        //0-255
        public byte ServiceType { get { return (byte)(packet_data[2]); } }
    }
}