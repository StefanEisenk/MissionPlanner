using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using OpenTK;

namespace UAVCAN
{
    public class UAVCAN
    {
        public class statetracking
        {
            public BigInteger bi = new BigInteger();
            public int bit = 0;
        }

        public static bool testconversion<T>(T input, byte bitlength, bool signed) where T : struct
        {
            var buf = new byte[8];
            T ans = input;
            uavcan.canardEncodeScalar(buf, 0, bitlength, input);
            uavcan.canardDecodeScalar(new uavcan.CanardRxTransfer(buf), 0, bitlength, signed, ref ans);

            if (input.ToString() != ans.ToString())
                throw new Exception();

            return true;
        }

        public static void update(string firmware_name)
        {
            var firmware_namebytes = ASCIIEncoding.ASCII.GetBytes(firmware_name);

            uavcan.uavcan_protocol_GetNodeInfo_res entry = new uavcan.uavcan_protocol_GetNodeInfo_res();
            //entry.name
            //entry.software_version.image_crc
            if (entry.status.mode != uavcan.UAVCAN_PROTOCOL_NODESTATUS_MODE_SOFTWARE_UPDATE)
            {
                var req_msg =
                    new uavcan.uavcan_protocol_file_BeginFirmwareUpdate_req()
                    {
                        image_file_remote_path = new uavcan.uavcan_protocol_file_Path()
                            {path = firmware_namebytes},
                        source_node_id = 126
                    };
                req_msg.image_file_remote_path.path_len = (byte) firmware_namebytes.Length;

                var state = new statetracking();
                req_msg.encode(chunk_cb, state);
            }
        }

        public static void test()
        {
            update("file");

            var fix = new uavcan.uavcan_equipment_gnss_Fix()
            {
                timestamp = new uavcan.uavcan_Timestamp() {usec = 1},
                gnss_timestamp = new uavcan.uavcan_Timestamp() {usec = 2},
                gnss_time_standard = 3,
                height_ellipsoid_mm = 4,
                height_msl_mm = 5,
                latitude_deg_1e8 = 6,
                longitude_deg_1e8 = 7,
                num_leap_seconds = 17,
                pdop = new Half(8),
                sats_used = 10, ned_velocity = new[] {new Half(1), new Half(2), new Half(3)}, status = 3
            };

            testconversion((byte) 3, 3, false);
            testconversion((byte) 3, 3, false);
            testconversion((sbyte) -3, 3, true);
            testconversion((byte) 3, 5, false);
            testconversion((sbyte) -3, 5, true);
            testconversion((sbyte) -3, 5, true);
            testconversion((ulong) 1234567890, 55, false);
            testconversion((ulong) 1234567890, 33, false);
            testconversion((long) -1234567890, 33, true);

            testconversion((int) -12345678, 27, true);
            testconversion((int) (1 << 25), 27, true);
            // will fail
            //testconversion((int)(1 << 26), 27, true);

            var state = new statetracking();


            fix.encode(chunk_cb, state);

            var data = state.bi.getBytes().Reverse().ToArray();

            Array.Resize(ref data, (state.bit + 7) / 8);

            var fixtest = new uavcan.uavcan_equipment_gnss_Fix();
            fixtest.decode(new uavcan.CanardRxTransfer(data));

            if (fix != fixtest)
            {

            }

            var lines = File.ReadAllLines(@"C:\Users\michael\OneDrive\canlog.can");
            var id_len = 0;

            // need sourcenode, msgid, transfer id

            Dictionary<(byte, int, int), List<byte>> transfer = new Dictionary<(byte, int, int), List<byte>>();

            foreach (var line in lines)
            {
                var line_len = line.Length;

                if (line_len == 0)
                    continue;

                if (line[0] == 'T')
                {
                    id_len = 8;
                }
                else if (line[0] == 't')
                {
                    id_len = 3;
                }
                else
                {
                    continue;
                }

                var packet_id = Convert.ToInt32(new string(line.Skip(1).Take(id_len).ToArray()), 16);
                var frame = new CANFrame(BitConverter.GetBytes(packet_id));
                var packet_len = line[1 + id_len] - 48;
                var with_timestamp = line_len > (2 + id_len + packet_len * 2);

                var packet_data = line.Skip(2 + id_len).Take(packet_len * 2).NowNextBy2().Select(a =>
                {
                    return Convert.ToByte(a.Item1 + "" + a.Item2, 16);
                });

                //Console.WriteLine(ASCIIEncoding.ASCII.GetString( packet_data));

                var payload = new CANPayload(packet_data.ToArray());

                if (payload.SOT)
                    transfer[(frame.SourceNode, frame.MsgTypeID, payload.TransferID)] = new List<byte>();

                // if have not seen SOT, abort
                if (!transfer.ContainsKey((frame.SourceNode, frame.MsgTypeID, payload.TransferID)))
                    continue;

                transfer[(frame.SourceNode, frame.MsgTypeID, payload.TransferID)].AddRange(payload.Payload);

                //todo check toggle

                if (payload.SOT && !payload.EOT)
                {
                    //todo first 2 bytes are checksum

                }

                if (payload.EOT)
                {
                    var result = transfer[(frame.SourceNode, frame.MsgTypeID, payload.TransferID)].ToArray();

                    transfer.Remove((frame.SourceNode, frame.MsgTypeID, payload.TransferID));

                    if (!uavcan.MSG_INFO.Any(a => a.Item2 == frame.MsgTypeID))
                        continue;

                    var msgtype = uavcan.MSG_INFO.First(a => a.Item2 == frame.MsgTypeID);

                    var dt_sig = BitConverter.GetBytes(msgtype.Item3);

                    //Array.Reverse(dt_sig);

                    var startbyte = 0;

                    if (!payload.SOT && payload.EOT)
                    {
                        startbyte = 2;

                        var payload_crc = result[0] | result[1] << 8;

                        var crcprocess = new TransferCRC();
                        crcprocess.add(dt_sig, 8);
                        crcprocess.add(result.Skip(startbyte).ToArray(), result.Length - startbyte);
                        var crc = crcprocess.get();

                        if (crc != payload_crc)
                        {
                            Console.WriteLine("Bad Message " + frame.MsgTypeID);
                            continue;
                        }
                    }
                    else
                    {

                    }

                    if (frame.MsgTypeID == uavcan.COM_HEX_EQUIPMENT_FLOW_MEASUREMENT_DT_ID)
                    {
                        var ans = result.ByteArrayToUAVCANMsg<uavcan.com_hex_equipment_flow_Measurement>(startbyte);
                    }
                    else if (frame.MsgTypeID == uavcan.UAVCAN_PROTOCOL_NODESTATUS_DT_ID)
                    {
                        try
                        {
                            var ans = result.ByteArrayToUAVCANMsg<uavcan.uavcan_protocol_GetNodeInfo_req>(startbyte);
                        }
                        catch
                        {
                        }
                    }
                    else if (frame.MsgTypeID == uavcan.UAVCAN_EQUIPMENT_RANGE_SENSOR_MEASUREMENT_DT_ID)
                    {
                        var ans =
                            result.ByteArrayToUAVCANMsg<uavcan.uavcan_equipment_range_sensor_Measurement>(startbyte);
                    }
                    else if (frame.MsgTypeID == uavcan.UAVCAN_EQUIPMENT_GNSS_FIX_DT_ID)
                    {
                        try
                        {
                            var ans = result.ByteArrayToUAVCANMsg<uavcan.uavcan_equipment_gnss_Fix>(startbyte);
                        }
                        catch
                        {
                        }
                    }
                    else
                    {
                        var type = uavcan.MSG_INFO.First(a => a.Item2 == frame.MsgTypeID).Item1;

                        Console.WriteLine(type);

                        MethodInfo method = typeof(Extension).GetMethod("ByteArrayToUAVCANMsg");
                        MethodInfo generic = method.MakeGenericMethod(type);
                        var ans = generic.Invoke(null, new object[] {result, startbyte});

                        Console.WriteLine(ans);
                    }
                }
            }
        }



        private static void chunk_cb(byte[] buffer, int sizeinbits, object ctx)
        {
            var stuff = (statetracking) ctx;
            if (buffer == null)
            {
                stuff.bit += sizeinbits;
                return;
            }

            BigInteger input = new BigInteger(buffer.Reverse().ToArray());

            for (uint a = 0; a < sizeinbits; a++)
            {
                if ((input & (1L << (int) a)) > 0)
                {
                    stuff.bi.setBit((uint) stuff.bit + a);
                }
            }

            stuff.bit += sizeinbits;
        }
    }

    // 29bit
}