using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using OpenTK;
using uint8_t = System.Byte;
using uint16_t = System.UInt16;
using uint32_t = System.UInt32;
using uint64_t = System.UInt64;

using int8_t = System.SByte;
using int16_t = System.Int16;
using int32_t = System.Int32;
using int64_t = System.Int64;

using float32 = System.Single;
using System.Collections;
using System.Linq;
using System.Runtime.CompilerServices;

public static class Extension
{
    public static T ByteArrayToUAVCANMsg<T>(this byte[] transfer, int startoffset) where T : new()
    {
        var ans = ((IUAVCANSerialize) new T());
        ans.decode(new uavcan.CanardRxTransfer(transfer.Skip(startoffset).ToArray()));
        return (T) ans;
    }

    public static IEnumerable<Tuple<T, T>> NowNextBy2<T>(this IEnumerable<T> list)
    {
        T now = default(T);
        T next = default(T);

        int a = -1;
        foreach (var item in list)
        {
            a++;
            now = next;
            next = item;
            if (a % 2 == 0)
                continue;
            yield return new Tuple<T, T>(now, next);
        }
    }
}

public interface IUAVCANSerialize
{
    void encode(uavcan.uavcan_serializer_chunk_cb_ptr_t chunk_cb, object ctx);

    void decode(uavcan.CanardRxTransfer transfer);
}

public partial class uavcan
{
    private static int CANARD_ERROR_INTERNAL = -1;

    public delegate void uavcan_serializer_chunk_cb_ptr_t(byte[] buffer, int sizeinbits, object ctx);

    [StructLayout(LayoutKind.Explicit, Size = 8, Pack = 1)]
    public struct union
    {
        [FieldOffset(0)] public bool boolean;

        ///< sizeof(bool) is implementation-defined, so it has to be handled separately
        [FieldOffset(0)] public uint8_t u8;

        ///< Also char
        [FieldOffset(0)] public int8_t s8;

        [FieldOffset(0)] public uint16_t u16;
        [FieldOffset(0)] public int16_t s16;
        [FieldOffset(0)] public uint32_t u32;
        [FieldOffset(0)] public int32_t s32;

        ///< Also float, possibly double, possibly long double (depends on implementation)
        [FieldOffset(0)] public uint64_t u64;

        [FieldOffset(0)] public int64_t s64;

        [FieldOffset(0)] public float f32;
        [FieldOffset(0)] public double d64;

        public uint8_t this[int index] {
            get { return BitConverter.GetBytes(u64)[index]; }
            set
            {
                var temp = BitConverter.GetBytes(u64);
                temp[index] = value;
                u64 = BitConverter.ToUInt64(temp, 0);
            }
        }

        ///< Also double, possibly float, possibly long double (depends on implementation)
        public IReadOnlyList<uint8_t> bytes
        {
            get { return BitConverter.GetBytes(u64); }
           /* set
            {
                var temp = value.ToArray();
                Array.Resize(ref temp, 8);
                u64 = BitConverter.ToUInt64(temp, 0);
            }*/
        }

        public union(bool b1 = false)
        {
            boolean = false;
            u8 = 0;
            s8 = 0;
            u16 = 0;
            u32 = 0;
            u64 = 0;
            s8 = 0;
            s16 = 0;
            s32 = 0;
            s64 = 0;
            f32 = 0;
            d64 = 0;
        }
    }



    public class CanardRxTransfer : IEnumerable<byte>
    {
        public uint32_t payload_len
        {
            get { return (uint32_t)data.Length; }
        }

        public byte[] data;

        public CanardRxTransfer(byte[] input)
        {
            data = input;
        }

        public byte this[int a]
        {
            get { return data[a]; }
        }

        public IEnumerator<byte> GetEnumerator()
        {
            return ((IEnumerable<byte>)data).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<byte>)data).GetEnumerator();
        }
    }

    public static void canardEncodeScalar<T>(byte[] destination,
        uint bit_offset,
        byte bit_length,
        T value)
    {
        union storage = new union(false);

        uint8_t std_byte_length = 0;

        // Extra most significant bits can be safely ignored here.
        if (bit_length == 1)
        {
            std_byte_length = sizeof(bool);
            storage.boolean = ((int) (dynamic) value) != 0;
        }
        else if (bit_length <= 8)
        {
            std_byte_length = 1;
            storage.u8 = ((uint8_t) (dynamic) value);
        }
        else if (bit_length <= 16)
        {
            std_byte_length = 2;
            storage.u16 = ((uint16_t) (dynamic) value);
        }
        else if (bit_length <= 32)
        {
            std_byte_length = 4;
            storage.u32 = ((uint32_t) (dynamic) value);
        }
        else if (bit_length <= 64)
        {
            std_byte_length = 8;
            storage.u64 = ((uint64_t) (dynamic) value);
        }

        copyBitArray(storage.bytes.ToArray(), 0, bit_length, destination, bit_offset);
    }

    private static void copyBitArray(


        byte[] src, uint32_t src_offset, uint32_t src_len, byte[] dst, uint32_t dst_offset)
    {
        CANARD_ASSERT(src_len > 0U);

        // Normalizing inputs
        //src += src_offset / 8;
        //dst += dst_offset / 8;

        //src_offset %= 8;
        //dst_offset %= 8;

        uint last_bit = src_offset + src_len;
        while (last_bit - src_offset > 0)
        {
             uint8_t src_bit_offset = (uint8_t)(src_offset % 8U);
             uint8_t dst_bit_offset = (uint8_t)(dst_offset % 8U);

             uint8_t max_offset = (uint8_t)Math.Max(src_bit_offset, dst_bit_offset);
             uint32_t copy_bits = (uint32_t)Math.Min(last_bit - src_offset, 8U - max_offset);

             uint8_t write_mask = (uint8_t)((uint8_t)(0xFFU >> (int)(8u-copy_bits)) >> dst_bit_offset);
             uint8_t src_data = (uint8_t)((src[src_offset / 8U] << src_bit_offset) >> dst_bit_offset);

            dst[dst_offset / 8U] = (uint8_t)((dst[dst_offset / 8U] & ~write_mask) | (src_data & write_mask));

            src_offset += copy_bits;
            dst_offset += copy_bits;
        }
    }

    public static void memset(byte[] buffer, int chartocopy, int size)
    {
        Array.Clear(buffer, 0, size);
    }

    public static int canardDecodeScalar<T>(CanardRxTransfer transfer,
        uint bit_offset,
        byte bit_length,
        bool value_is_signed,
        ref T out_value)
    {

        union storage = new union(false);

        memset(storage.bytes.ToArray(), 0, Marshal.SizeOf(storage)); // This is important

        int result = descatterTransferPayload(transfer, bit_offset, bit_length, ref storage);
        if (result <= 0)
        {
            return result;
        }

        CANARD_ASSERT((result > 0) && (result <= 64) && (result <= bit_length));

        /*
         * The bit copy algorithm assumes that more significant bits have lower index, so we need to shift some.
         * Extra most significant bits will be filled with zeroes, which is fine.
         * Coverity Scan mistakenly believes that the array may be overrun if bit_length == 64; however, this branch will
         * not be taken if bit_length == 64, because 64 % 8 == 0.
         */
        if ((bit_length % 8) != 0)
        {
            // coverity[overrun-local]
            //storage[bit_length / 8] = (uint8_t) (storage.bytes[bit_length / 8] >> ((8 - (bit_length % 8)) & 7));
        }

        /*
         * Determining the closest standard byte length - this will be needed for byte reordering and sign bit extension.
         */
        uint8_t std_byte_length = 0;
        if (bit_length == 1)
        {
            std_byte_length = sizeof(bool);
        }
        else if (bit_length <= 8)
        {
            std_byte_length = 1;
        }
        else if (bit_length <= 16)
        {
            std_byte_length = 2;
        }
        else if (bit_length <= 32)
        {
            std_byte_length = 4;
        }
        else if (bit_length <= 64)
        {
            std_byte_length = 8;
        }
        else
        {
            CANARD_ASSERT(false);
            return -CANARD_ERROR_INTERNAL;
        }

        CANARD_ASSERT((std_byte_length > 0) && (std_byte_length <= 8));

        /*
         * Flipping the byte order if needed.
         */
        /*if (isBigEndian())
        {
            swapByteOrder(&storage.bytes[0], std_byte_length);
        }*/

        /*
         * Extending the sign bit if needed. I miss templates.
         */
        if (value_is_signed && (std_byte_length * 8 != bit_length))
        {
            if (bit_length <= 8)
            {
                if ((storage.s8 & (1U << (bit_length - 1))) != 0) // If the sign bit is set...
                {
                    storage.u8 |=
                        (byte) ((uint8_t) 0xFFU & (uint8_t) ~((1 << bit_length) - 1U)); // ...set all bits above it.
                }
            }
            else if (bit_length <= 16)
            {
                if ((storage.s16 & (1U << (bit_length - 1))) != 0)
                {
                    storage.u16 |= (UInt16) ((uint16_t) 0xFFFFU & (uint16_t) ~((1 << bit_length) - 1U));
                }
            }
            else if (bit_length <= 32)
            {
                if ((storage.s32 & (((uint32_t) 1) << (bit_length - 1))) != 0)
                {
                    storage.u32 |= (uint32_t) 0xFFFFFFFFU & (uint32_t) ~((((uint32_t) 1U) << bit_length) - 1U);
                }
            }
            else if (bit_length < 64) // Strictly less, this is not a typo
            {
                if ((storage.u64 & (((uint64_t) 1) << (bit_length - 1))) != 0)
                {
                    storage.u64 |= (uint64_t) 0xFFFFFFFFFFFFFFFFU & (uint64_t) ~((((uint64_t) 1) << bit_length) - 1U);
                }
            }
            else
            {
                CANARD_ASSERT(false);
                return -CANARD_ERROR_INTERNAL;
            }
        }

        /*
         * Copying the result out.
         */
        if (value_is_signed)
        {
            if (bit_length <= 8)
            {
                out_value = (T) (IConvertible) storage.s8;
            }
            else if (bit_length <= 16)
            {
                out_value = (T) (dynamic) storage.s16;
            }
            else if (typeof(T) == typeof(float))
            {
                out_value = (T) (IConvertible) storage.f32;
            }
            else if (bit_length <= 32)
            {
                out_value = (T) (IConvertible) storage.s32;
            }
            else if (bit_length <= 64)
            {
                out_value = (T) (IConvertible) storage.s64;
            }
            else
            {
                CANARD_ASSERT(false);
                return -CANARD_ERROR_INTERNAL;
            }
        }
        else
        {
            if (bit_length == 1)
            {
                out_value = (T) (IConvertible) storage.boolean;
            }
            else if (bit_length <= 8)
            {
                out_value = (T) (IConvertible) storage.u8;
            }
            else if (bit_length <= 16)
            {
                out_value = (T) (IConvertible) storage.u16;
            }
            else if (bit_length <= 32)
            {
                out_value = (T) (IConvertible) storage.u32;
            }
            else if (bit_length <= 64)
            {
                out_value = (T) (IConvertible) storage.u64;
            }
            else
            {
                CANARD_ASSERT(false);
                return -CANARD_ERROR_INTERNAL;
            }
        }

        CANARD_ASSERT(result <= bit_length);
        CANARD_ASSERT(result > 0);
        return result;
    }

    private static int descatterTransferPayload(CanardRxTransfer transfer, uint bit_offset, byte bit_length 
        , ref union output  )
    {
        if (bit_offset >= (transfer.payload_len * 8))
        {
            return 0;       // Out of range, reading zero bits
        }

        if (bit_offset + bit_length > (transfer.payload_len * 8))
            bit_length = (uint8_t)(transfer.payload_len * 8 - bit_offset);

        BigInteger bi = new BigInteger(transfer.data.Reverse().ToArray());
        
        var newbi = bi >> (int32_t)bit_offset;

        for (int a = bit_length; a < 64; a++)
        {
            newbi.unsetBit((uint)a);
        }

        output.s64 = newbi.LongValue() ;
        var test = newbi.getBytes().Reverse().Take((bit_length / 8) + 1).ToArray();
        //Array.Resize(ref test,8);
        //output.u64 = BitConverter.ToUInt64(test, 0);

        return bit_length;
    }

    private static void CANARD_ASSERT(bool p)
    {
        if (p == false)
            throw new ArgumentException();
    }

    private static UInt16 canardConvertNativeFloatToFloat16(Single flo)
    {
        return BitConverter.ToUInt16(Half.GetBytes(new Half(flo)),0);
    }


    private static Half canardConvertFloat16ToNativeFloat(ushort float16Val)
    {
        return Half.FromBytes(BitConverter.GetBytes(float16Val),0);
    }
}
