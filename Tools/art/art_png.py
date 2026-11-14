"""PNG writer/provenance for original generated v14 bitmap assets (stdlib only)."""
import struct, zlib
from pathlib import Path
SIGNATURE=b'\x89PNG\r\n\x1a\n'
def chunk(kind, data):
 return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
def mark(path):
 path=Path(path);raw=path.read_bytes();assert raw.startswith(SIGNATURE)
 text=b'GeneratedBy\0CodeRabbit; original v14 game art'
 if text not in raw:path.write_bytes(raw[:33]+chunk(b'tEXt',text)+raw[33:])
def write(path, pixels, width, height):
 rows=b''.join(b'\0'+bytes(pixels[y*width*4:(y+1)*width*4]) for y in range(height))
 Path(path).write_bytes(SIGNATURE+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(rows,9))+chunk(b'IEND',b''));mark(path)
