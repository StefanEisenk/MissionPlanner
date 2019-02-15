
using uint8_t = System.Byte;
using uint16_t = System.UInt16;
using uint32_t = System.UInt32;
using uint64_t = System.UInt64;

using int8_t = System.SByte;
using int16_t = System.Int16;
using int32_t = System.Int32;
using int64_t = System.Int64;

using float32 = System.Single;

using System;
using System.Runtime.InteropServices;
using OpenTK;


public partial class uavcan {



/*

static uavcan_message_descriptor_s uavcan_Measurement_descriptor = {
    UAVCAN_MEASUREMENT_DT_SIG,
    UAVCAN_MEASUREMENT_DT_ID,
    CanardTransferTypeBroadcast,
    sizeof(uavcan_Measurement),
    UAVCAN_MEASUREMENT_MAX_PACK_SIZE,
    encode_func,
    decode_func,
    null
};
*/

static void encode_uavcan_Measurement(uavcan_Measurement msg, uavcan_serializer_chunk_cb_ptr_t chunk_cb, object ctx) {
    uint8_t[] buffer = new uint8_t[8];
    _encode_uavcan_Measurement(buffer, msg, chunk_cb, ctx, true);
}

static uint32_t decode_uavcan_Measurement(CanardRxTransfer transfer, uavcan_Measurement msg) {
    uint32_t bit_ofs = 0;
    _decode_uavcan_Measurement(transfer, ref bit_ofs, msg, true);
    return (bit_ofs+7)/8;
}

static void _encode_uavcan_Measurement(uint8_t[] buffer, uavcan_Measurement msg, uavcan_serializer_chunk_cb_ptr_t chunk_cb, object ctx, bool tao) {

    memset(buffer,0,8);
    canardEncodeScalar(buffer, 0, 32, msg.integration_interval);
    chunk_cb(buffer, 32, ctx);
    for (int i=0; i < 2; i++) {
            memset(buffer,0,8);
            canardEncodeScalar(buffer, 0, 32, msg.rate_gyro_integral[i]);
            chunk_cb(buffer, 32, ctx);
    }
    for (int i=0; i < 2; i++) {
            memset(buffer,0,8);
            canardEncodeScalar(buffer, 0, 32, msg.flow_integral[i]);
            chunk_cb(buffer, 32, ctx);
    }
    memset(buffer,0,8);
    canardEncodeScalar(buffer, 0, 8, msg.quality);
    chunk_cb(buffer, 8, ctx);
}

static void _decode_uavcan_Measurement(CanardRxTransfer transfer,ref uint32_t bit_ofs, uavcan_Measurement msg, bool tao) {

    canardDecodeScalar(transfer, bit_ofs, 32, true, ref msg.integration_interval);
    bit_ofs += 32;

/*['__doc__', '__init__', '__module__', '__repr__', '__str__', 'get_normalized_definition', 'name', 'type']*/
    for (int i=0; i < 2; i++) {
        canardDecodeScalar(transfer, bit_ofs, 32, true, ref msg.rate_gyro_integral[i]);
        bit_ofs += 32;
    }

/*['__doc__', '__init__', '__module__', '__repr__', '__str__', 'get_normalized_definition', 'name', 'type']*/
    for (int i=0; i < 2; i++) {
        canardDecodeScalar(transfer, bit_ofs, 32, true, ref msg.flow_integral[i]);
        bit_ofs += 32;
    }

    canardDecodeScalar(transfer, bit_ofs, 8, false, ref msg.quality);
    bit_ofs += 8;

}

}