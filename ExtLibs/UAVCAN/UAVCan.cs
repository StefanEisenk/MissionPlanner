using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;


namespace UAVCAN
{
    public class UAVCAN
    {
        public class statetracking
        {
            public BigInteger bi = new BigInteger();
            public int bit = 0;

            public byte[] ToBytes()
            {
                int get = (bit / 32) + 1;
                
                System.Numerics.BigInteger sbi = System.Numerics.BigInteger.Zero;

                for (int a = 0; a < get; a++)
                {
                    sbi += new System.Numerics.BigInteger(bi.data[a]) << (a * 32);
                }
                //bi.data

                var data2 = sbi.ToByteArray();

                Array.Resize(ref data2, (bit + 7) / 8);

                return data2;
            }
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

        public delegate void MessageRecievedDel(CANFrame frame, object msg);

        public event MessageRecievedDel MessageReceived;

        private object sr_lock = new object();
        private StreamReader sr;
        DateTime uptime = DateTime.Now;

        /// <summary>
        /// Start slcan stream sending a nodestatus packet every second
        /// </summary>
        /// <param name="stream"></param>
        public void StartSLCAN(Stream stream)
        {
            stream.Write(new byte[] { (byte)'O', (byte)'\r' }, 0, 2);
            sr = new StreamReader(stream);

            // read everything
            Task.Run(() =>
            {
                while (!sr.EndOfStream)
                {
                    var line = sr.ReadLine();
                    try
                    {
                        ReadMessage(line);
                    }
                    catch
                    {
                    }
                }
            });

            // 1 second nodestatus send
            Task.Run(() => {
                while (sr.BaseStream.CanWrite)
                {
                    var slcan = PackageMessage(SourceNode, 20,
                        new uavcan.uavcan_protocol_NodeStatus()
                            {health = (byte)uavcan.UAVCAN_PROTOCOL_NODESTATUS_HEALTH_OK, mode = (byte)uavcan.UAVCAN_PROTOCOL_NODESTATUS_MODE_OPERATIONAL, sub_mode = 0, uptime_sec = (uint)(DateTime.Now - uptime).TotalSeconds, vendor_specific_status_code = 0});

                    lock (sr_lock)
                        sr.BaseStream.Write(ASCIIEncoding.ASCII.GetBytes(slcan), 0, slcan.Length);

                    Thread.Sleep(1000);
                }
            });
        }

        public void update(string firmware_name)
        {
            var firmware_namebytes = ASCIIEncoding.ASCII.GetBytes(Path.GetFileName(firmware_name.ToLower()));

            List<int> nodeList = new List<int>();

            List<byte> dynamicBytes = new List<byte>();

            using (var file = File.OpenRead(firmware_name))
            {
                MessageReceived += (frame, msg) =>
                {
                    if (msg.GetType() == typeof(uavcan.uavcan_protocol_file_BeginFirmwareUpdate_res))
                    {
                        var bfures = msg as uavcan.uavcan_protocol_file_BeginFirmwareUpdate_res;
                        if (bfures.error != 0)
                            throw new Exception("Begin Firmware Update returned an error");
                    }
                    else if (msg.GetType() == typeof(uavcan.uavcan_protocol_NodeStatus))
                    {
                        if (!nodeList.Contains(frame.SourceNode))
                            nodeList.Add(frame.SourceNode);
                    }
                    else if (msg.GetType() == typeof(uavcan.uavcan_protocol_file_Read_req))
                    {
                        var frreq = msg as uavcan.uavcan_protocol_file_Read_req;
                        if (ASCIIEncoding.ASCII.GetString(frreq.path.path).TrimEnd('\0') !=
                            ASCIIEncoding.ASCII.GetString(firmware_namebytes))
                            throw new Exception("File read request for file we are not serving " +
                                                ASCIIEncoding.ASCII.GetString(frreq.path.path).TrimEnd('\0') + " vs " +
                                                ASCIIEncoding.ASCII.GetString(firmware_namebytes));
                        Console.WriteLine("file_Read: {0} at {1}", ASCIIEncoding.ASCII.GetString(frreq.path.path).TrimEnd('\0'), frreq.offset);
                        file.Seek((long) frreq.offset, SeekOrigin.Begin);
                        var buffer = new byte[256];
                        var read = file.Read(buffer, 0, 256);
                        var readRes = new uavcan.uavcan_protocol_file_Read_res()
                        {
                            data = buffer,
                            data_len = (ushort) read,
                            error = new uavcan.uavcan_protocol_file_Error()
                                {value = (short) uavcan.UAVCAN_PROTOCOL_FILE_ERROR_OK}
                        };

                        var slcan = PackageMessage(frame.SourceNode, frame.Priority, readRes);

                        lock (sr_lock)
                        {
                            sr.BaseStream.Write(ASCIIEncoding.ASCII.GetBytes(slcan), 0, slcan.Length);
                        }
                    }
                    else if (msg.GetType() == typeof(uavcan.uavcan_protocol_GetNodeInfo_res))
                    {
                        var gnires = msg as uavcan.uavcan_protocol_GetNodeInfo_res;
                        Console.WriteLine("GetNodeInfo: seen '{0}'", ASCIIEncoding.ASCII.GetString(gnires.name));
                        if (gnires.status.mode != uavcan.UAVCAN_PROTOCOL_NODESTATUS_MODE_SOFTWARE_UPDATE)
                        {
                            var req_msg =
                                new uavcan.uavcan_protocol_file_BeginFirmwareUpdate_req()
                                {
                                    image_file_remote_path = new uavcan.uavcan_protocol_file_Path()
                                        {path = firmware_namebytes},
                                    source_node_id = SourceNode
                                };
                            req_msg.image_file_remote_path.path_len = (byte) firmware_namebytes.Length;

                            var slcan = PackageMessage(frame.SourceNode, frame.Priority, req_msg);
                            lock (sr_lock)
                                sr.BaseStream.Write(ASCIIEncoding.ASCII.GetBytes(slcan), 0, slcan.Length);
                        }
                    }
                    else if (msg.GetType() == typeof(uavcan.uavcan_protocol_dynamic_node_id_Allocation))
                    {
                        var allocation = msg as uavcan.uavcan_protocol_dynamic_node_id_Allocation;

                        if (allocation.first_part_of_unique_id)
                        {
                            // first part of id
                            allocation.first_part_of_unique_id = false;
                            dynamicBytes.Clear();
                            dynamicBytes.AddRange(allocation.unique_id.Take(allocation.unique_id_len));

                            var slcan = PackageMessage(SourceNode, frame.Priority, allocation);
                            lock (sr_lock)
                                sr.BaseStream.Write(ASCIIEncoding.ASCII.GetBytes(slcan), 0, slcan.Length);
                        }
                        else
                        {
                            allocation.first_part_of_unique_id = false;
                            dynamicBytes.AddRange(allocation.unique_id.Take(allocation.unique_id_len));
                            allocation.unique_id = dynamicBytes.ToArray();
                            allocation.unique_id_len = (byte)allocation.unique_id.Length;
                            if (allocation.unique_id_len >= 16)
                            {
                                for (int a = 125; a >= 1; a--)
                                {
                                    if (!nodeList.Contains(a))
                                    {
                                        allocation.node_id = (byte) a;
                                        break;
                                    }
                                }
                                dynamicBytes.Clear();
                            }
                            var slcan = PackageMessage(SourceNode, frame.Priority, allocation);
                            lock (sr_lock)
                                sr.BaseStream.Write(ASCIIEncoding.ASCII.GetBytes(slcan), 0, slcan.Length);
                        }
                    }
                };

                var statetracking = new statetracking();

                // wait to build nodelist
                Thread.Sleep(5000);

          

                // start readloop
                //uavcan.uavcan_protocol_file_Read_req

                while (true)
                {
                    foreach (var i in nodeList)
                    {
                        // get node info
                        uavcan.uavcan_protocol_GetNodeInfo_req gnireq = new uavcan.uavcan_protocol_GetNodeInfo_req() { };
                        gnireq.encode(chunk_cb, statetracking);

                        var slcan = PackageMessage((byte)i, 30, gnireq);
                        lock (sr_lock)
                            sr.BaseStream.Write(ASCIIEncoding.ASCII.GetBytes(slcan), 0, slcan.Length);
                    }

                    Thread.Sleep(10000);
                }
            }
        }

        public string PackageMessage(byte destNode, byte priority, IUAVCANSerialize msg)
        {
            var state = new statetracking();
            msg.encode(chunk_cb, state);

            var msgtype = uavcan.MSG_INFO.First(a => a.Item1 == msg.GetType());

            CANFrame cf = new CANFrame(new byte[4]);
            cf.SourceNode = SourceNode;
            cf.Priority = priority;
            
            if (msg.GetType().FullName.EndsWith("_res") || msg.GetType().FullName.EndsWith("_req"))
            {
                // service
                cf.IsServiceMsg = true;
                cf.SvcDestinationNode = destNode;
                cf.SvcIsRequest = msg.GetType().FullName.EndsWith("_req") ? true : false;
                cf.SvcTypeID = (byte)msgtype.Item2;
            }
            else
            {
                // message
                cf.MsgTypeID = (ushort)msgtype.Item2;
            }

            string ans = "";

            var payloaddata = state.ToBytes();     

            if (payloaddata.Length > 7)
            {
                var dt_sig = BitConverter.GetBytes(msgtype.Item3);

                var crcprocess = new TransferCRC();
                crcprocess.add(dt_sig, 8);
                crcprocess.add(payloaddata, payloaddata.Length);
                var crc = crcprocess.get();

                var buffer = new byte[8];
                var toogle = false;
                var size = 7;
                for (int a = 0; a < payloaddata.Length; a += size)
                {
                    if (a == 0)
                    {
                        buffer[0] = (byte) (crc & 0xff);
                        buffer[1] = (byte) (crc >> 8);
                        size = 5;
                        Array.ConstrainedCopy(payloaddata, a, buffer, 2, 5);
                    }
                    else
                    {
                        size = payloaddata.Length - a <= 7 ? payloaddata.Length - a : 7;
                        Array.ConstrainedCopy(payloaddata, a, buffer, 0, size);
                        if (buffer.Length != size + 1)
                            Array.Resize(ref buffer, size + 1);
                    }
                    CANPayload payload = new CANPayload(buffer);
                    payload.SOT = a == 0 ? true : false;
                    payload.EOT = a + size == payloaddata.Length ? true : false;
                    payload.TransferID = (byte)transferID;
                    payload.Toggle = toogle;
                    toogle = !toogle;

                    ans += String.Format("T{0}{1}{2}\r", cf.ToHex(), a == 0 ? 8 : size + 1, payload.ToHex());
                }
            }
            else
            {
                var buffer = new byte[payloaddata.Length + 1];
                Array.Copy(payloaddata, buffer, payloaddata.Length);
                CANPayload payload = new CANPayload(buffer);
                payload.SOT = payload.EOT = true;
                payload.TransferID = (byte)transferID;

                ans = String.Format("T{0}{1}{2}\r", cf.ToHex(), buffer.Length, payload.ToHex());
            }

            transferID++;

            Console.WriteLine("TX "+ans.Replace("\r","\r\n"));
            return ans;
        }

        Dictionary<(uint, int), List<byte>> transfer = new Dictionary<(uint, int), List<byte>>();

        public byte SourceNode { get; set; } = 127;

        private int transferID = 0;

        public UAVCAN(Byte sourceNode)
        {
            SourceNode = sourceNode;
        }

        public UAVCAN()
        {

        }

        public static void test()
        {
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
                pdop = 8f,
                sats_used = 10, ned_velocity = new[] {1f,2f,3f}, status = 3
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

            var data = state.ToBytes();//
            var data2 = state.bi.getBytes().Reverse().ToArray();

            Array.Resize(ref data2, (state.bit + 7) / 8);

            var fixtest = new uavcan.uavcan_equipment_gnss_Fix();
            fixtest.decode(new uavcan.CanardRxTransfer(data));

            if (fix != fixtest)
            {

            }

            {
                var lines = File.ReadAllLines(@"C:\Users\mich1\OneDrive\canlog gpsupdate2-8mhz.txt");
                
                var basecan = new UAVCAN();

                int l = 0;
                foreach (var line in lines)
                {
                    l++;

                    // tab delimiter file
                    var splitline = line.Split('\t');
                    
                    for (int a = 0; a < splitline.Length; a++)
                    {
                        splitline[a] = splitline[a].Trim().Replace(" ", "");
                    }

                    basecan.ReadMessage("T" + splitline[2] + (splitline[3].Length / 2) + splitline[3]);
                }
            }

            {
                var lines = File.ReadAllLines(@"C:\Users\michael\OneDrive\canlog.can");
                var id_len = 0;

                // need sourcenode, msgid, transfer id


                var basecan = new UAVCAN();

                int l = 0;
                foreach (var line in lines)
                {
                    l++;

                    basecan.ReadMessage(line);
                }
            }
        }

        public void ReadMessage(string line)
        {
            int id_len;
            var line_len = line.Length;

            if (line_len <= 4)
                return;


            if (line[0] == 'T') // 29 bit data frame
            {
                id_len = 8;
            }
            else if (line[0] == 't') // 11 bit data frame
            {
                id_len = 3;
            }
            else
            {
                return;
            }

            //T12ABCDEF2AA55 : extended can_id 0x12ABCDEF, can_dlc 2, data 0xAA 0x55
            var packet_id = Convert.ToUInt32(new string(line.Skip(1).Take(id_len).ToArray()), 16); // id
            var packet_len = line[1 + id_len] - 48; // dlc
            var with_timestamp = line_len > (2 + id_len + packet_len * 2);

            if (packet_len == 0)
                return;

            var frame = new CANFrame(BitConverter.GetBytes(packet_id));

            var packet_data = line.Skip(2 + id_len).Take(packet_len * 2).NowNextBy2().Select(a =>
            {
                return Convert.ToByte(a.Item1 + "" + a.Item2, 16);
            });

            //Console.WriteLine(ASCIIEncoding.ASCII.GetString( packet_data));
            Console.WriteLine("RX " + line.Replace("\r","\r\n"));

            var payload = new CANPayload(packet_data.ToArray());

            if (payload.SOT)
                transfer[(packet_id, payload.TransferID)] = new List<byte>();

            // if have not seen SOT, abort
            if (!transfer.ContainsKey((packet_id, payload.TransferID)))
                return;

            transfer[(packet_id, payload.TransferID)].AddRange(payload.Payload);

            //todo check toggle

            if (payload.SOT && !payload.EOT)
            {
                //todo first 2 bytes are checksum
            }

            if (payload.EOT)
            {
                var result = transfer[(packet_id, payload.TransferID)].ToArray();

                transfer.Remove((packet_id, payload.TransferID));

                if (frame.TransferType == CANFrame.FrameType.anonymous)
                {
                    // dynamic node allocation
                    if (!uavcan.MSG_INFO.Any(a =>
                        a.Item2 == frame.MsgTypeID && frame.TransferType == CANFrame.FrameType.anonymous &&
                        !a.Item1.Name.EndsWith("_req") && !a.Item1.Name.EndsWith("_res")))
                    {
                        Console.WriteLine("No Message ID " + frame.SvcTypeID);
                        return;
                    }
                }

                if (frame.TransferType == CANFrame.FrameType.service)
                {
                    if (!uavcan.MSG_INFO.Any(a =>
                        a.Item2 == frame.SvcTypeID && frame.TransferType == CANFrame.FrameType.service))
                    {
                        Console.WriteLine("No Message ID " + frame.SvcTypeID);
                        return;
                    }
                }

                if (frame.TransferType == CANFrame.FrameType.message)
                {
                    if (!uavcan.MSG_INFO.Any(a =>
                        a.Item2 == frame.MsgTypeID && frame.TransferType == CANFrame.FrameType.message))
                    {
                        Console.WriteLine("No Message ID " + frame.MsgTypeID);
                        return;
                    }
                }

                var msgtype = uavcan.MSG_INFO.First(a =>
                    a.Item2 == frame.MsgTypeID && frame.TransferType == CANFrame.FrameType.message &&
                    !a.Item1.Name.EndsWith("_req") && !a.Item1.Name.EndsWith("_res") ||
                    a.Item2 == frame.MsgTypeID && frame.TransferType == CANFrame.FrameType.anonymous &&
                    !a.Item1.Name.EndsWith("_req") && !a.Item1.Name.EndsWith("_res") ||
                    a.Item2 == frame.SvcTypeID && frame.TransferType == CANFrame.FrameType.service &&
                    frame.SvcIsRequest && a.Item1.Name.EndsWith("_req") ||
                    a.Item2 == frame.SvcTypeID && frame.TransferType == CANFrame.FrameType.service &&
                    !frame.SvcIsRequest && a.Item1.Name.EndsWith("_res"));

                var dt_sig = BitConverter.GetBytes(msgtype.Item3);

                var startbyte = 0;

                if (!payload.SOT && payload.EOT)
                {
                    startbyte = 2;

                    if (result.Length <= 1)
                        return;

                    var payload_crc = result[0] | result[1] << 8;

                    var crcprocess = new TransferCRC();
                    crcprocess.add(dt_sig, 8);
                    crcprocess.add(result.Skip(startbyte).ToArray(), result.Length - startbyte);
                    var crc = crcprocess.get();

                    if (crc != payload_crc)
                    {
                        Console.WriteLine("Bad Message " + frame.MsgTypeID);
                        return;
                    }
                }
                else
                {
                }

                //Console.WriteLine(msgtype);

                MethodInfo method = typeof(Extension).GetMethod("ByteArrayToUAVCANMsg");
                MethodInfo generic = method.MakeGenericMethod(msgtype.Item1);
                try
                {
                    var ans = generic.Invoke(null, new object[] {result, startbyte});

                    MessageReceived?.Invoke(frame, ans);

                    //Console.WriteLine((frame.SourceNode == 127 ? "TX" : "RX") + " " + msgtype.Item1 + " " +JsonConvert.SerializeObject(ans));
                }
                catch
                {
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
            /*
            BigInteger input = new BigInteger(buffer.ToArray());

            for (uint a = 0; a < sizeinbits; a++)
            {
                if ((input & (1L << (int) a)) > 0)
                {
                    stuff.bi += BigInteger.One << (int) (stuff.bit + a);
                }
            }
            */

            //todo try replace this with built in dot net type Biginterger
        
      
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
}