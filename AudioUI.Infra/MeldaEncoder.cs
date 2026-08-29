using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioUI{

    public class MeldaEncoder{
        public static string EncodeMeldaChunk(string header, List<MeldaEntry> entries){
            using(MemoryStream ms = new MemoryStream())
            using(BinaryWriter writer = new BinaryWriter(ms)){
                writer.Write((byte)0);
                WriteStringBytes(writer, header);
                writer.Write((byte)0);

                foreach(var entry in entries){
                    WriteStringBytes(writer, entry.RawKey);
                    writer.Write((byte)0);

                    object val = entry.Value;
                    char typeCh;

                    if(val is double || val is float){
                        typeCh = 'd';
                        writer.Write((byte)typeCh);
                        writer.Write(Convert.ToDouble(val)); 
                    }else if(val is bool || (IsInteger(val) && Convert.ToInt64(val) < 256 && Convert.ToInt64(val) >= 0)){
                        typeCh = '1';
                        writer.Write((byte)typeCh);
                        writer.Write((byte)(Convert.ToInt32(val) & 0xFF));
                    }else if(val is string strVal){
                        typeCh = 's';
                        writer.Write((byte)typeCh);
                        WriteStringBytes(writer, strVal);
                        writer.Write((byte)0);
                    }else if(IsInteger(val)){
                        typeCh = '4';
                        writer.Write((byte)typeCh);
                        writer.Write(Convert.ToUInt32(val));
                    }else if(val == null){
                        typeCh = 'A';
                        writer.Write((byte)typeCh);
                    }
                }

                writer.Write(new byte[] { 0x2f, 0x2f, 0x7e });
                byte[] rawData = ms.ToArray();
                return CompressAndBase64(rawData);
            }
        }

        private static void WriteStringBytes(BinaryWriter writer, string text){
            if(!string.IsNullOrEmpty(text)){
                byte[] bytes = Encoding.ASCII.GetBytes(text);
                writer.Write(bytes);
            }
        }

        private static bool IsInteger(object obj){
            return obj is int || obj is long || obj is short || obj is byte || obj is uint || obj is ulong;
        }

        private static string CompressAndBase64(byte[] data){
            using(MemoryStream outputMs = new MemoryStream()){
                using(ZLibStream zlibStream = new ZLibStream(outputMs, CompressionLevel.SmallestSize)){
                    zlibStream.Write(data, 0, data.Length);
                }
                return Convert.ToBase64String(outputMs.ToArray());
            }
        }
    }
}

// original python
/*
def encode_melda_chunk(header: str, entries: List[Dict[str, Any]]) -> str:
    byte_data = bytearray([0])
    byte_data.extend(header.encode("ascii"))
    byte_data.append(0)
    
    for i in entries:
        byte_data.extend(i['raw_key'].encode("ascii"))
        byte_data.append(0)
        
        type_ch = ''
        if isinstance(i["value"], float):
            type_ch = 'd'
            byte_data.append(ord(type_ch))
            byte_data.extend(struct.pack("<d", i["value"]))
        elif isinstance(i["value"], bool) or isinstance(i["value"], int) and i["value"] < 256:
            type_ch = '1'
            byte_data.append(ord(type_ch))
            byte_data.append(int(i["value"]) & 0xFF)
        elif isinstance(i["value"], str):
            type_ch = 's'
            byte_data.append(ord(type_ch))
            byte_data.extend(i["value"].encode("ascii"))
            byte_data.append(0)
        elif isinstance(i["value"], int):
            type_ch = '4'
            byte_data.append(ord(type_ch))
            byte_data.extend(struct.pack("<I", i["value"]))
        elif isinstance(i["value"], type(None)):
            type_ch = 'A'
            byte_data.append(ord(type_ch))
    
    byte_data.extend(b'\x2f\x2f\x7e')
    c = zlib.compressobj(level=9, method=zlib.DEFLATED, wbits=15, memLevel=8, strategy=zlib.Z_DEFAULT_STRATEGY)
    data_compressed = c.compress(bytes(byte_data)) + c.flush()
    
    return base64.b64encode(data_compressed).decode("ascii")
*/