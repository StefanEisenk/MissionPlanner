using System.Linq;

namespace UAVCAN
{
    /// <summary>
    /// https://uavcan.org/Specification/4._CAN_bus_transport_layer/
    /// </summary>
    public class CANPayload
    {
        public byte[] packet_data;

        public CANPayload(byte[] packet_data)
        {
            this.packet_data = packet_data;
        }

        //0-31
        public byte TransferID
        {
            get { return (byte)(packet_data[packet_data.Length-1] & 0x1f); }
        }
        public bool Toggle { get { return (packet_data[packet_data.Length - 1] & 0x20) > 0; } }
        public bool EOT { get { return (packet_data[packet_data.Length - 1] & 0x40) > 0; } }
        public bool SOT { get { return (packet_data[packet_data.Length - 1] & 0x80) > 0; } }

        public byte[] Payload
        {
            get { return packet_data.Take(packet_data.Length - 1).ToArray(); }
        }
    }
}