"""Bounded, uncompressed 2D DDS SOURCE decoder for build-time embedded texture assets."""
from dataclasses import dataclass
from pathlib import Path
import struct

@dataclass(frozen=True)
class DdsSource:
    width: int
    height: int
    format: str
    pixels: bytes

# D3D9 legacy floating point FOURCC and DX10 DXGI formats.
LEGACY={113:('R16G16B16A16_FLOAT',8),114:('R32_FLOAT',4),115:('R32G32_FLOAT',8),116:('R32G32B32A32_FLOAT',16),111:('R16_FLOAT',2),112:('R16G16_FLOAT',4)}
DXGI={2:('R32G32B32A32_FLOAT',16),10:('R16G16B16A16_FLOAT',8),16:('R32G32_FLOAT',8),28:('R8G8B8A8_UNORM',4),34:('R16G16_FLOAT',4),41:('R32_FLOAT',4),49:('R8G8_UNORM',2),54:('R16_FLOAT',2),61:('R8_UNORM',1)}

def decode(data,name='<dds>'):
    def fail(message):raise ValueError(f'{name}: {message}')
    if len(data)<128 or len(data)>64*1024*1024 or data[:4]!=b'DDS ':fail('Invalid DDS header/size')
    u=lambda offset:struct.unpack_from('<I',data,offset)[0]
    if u(4)!=124 or u(76)!=32:fail('Invalid DDS structure sizes')
    flags,height,width,pitch,depth,mips=struct.unpack_from('<6I',data,8)
    if flags&0x1007!=0x1007 or not u(108)&0x1000:fail('DDS is missing required dimensions/pixel format/caps')
    if not 1<=width<=16384 or not 1<=height<=16384 or width*height>16777216:fail('DDS dimensions exceed texture limits')
    if mips not in (0,1) or depth not in (0,1) or u(112)!=0:fail('Only a single 2D image is supported (no array/cube/volume/mips)')
    pixel_flags,code,bits,r,g,b,a=struct.unpack_from('<7I',data,80)
    offset=128
    if pixel_flags&4:
        if code==struct.unpack('<I',b'DX10')[0]:
            if len(data)<148:fail('Truncated DDS DX10 header')
            format_code,dimension,misc,layers,alpha=struct.unpack_from('<5I',data,128)
            if dimension!=3 or misc&4 or layers!=1 or alpha&~7:fail('Unsupported DDS DX10 image kind')
            format_info=DXGI.get(format_code);offset=148
        else:format_info=LEGACY.get(code)
        if format_info is None:fail('Unsupported/compressed DDS format')
    elif pixel_flags&0x20000 and bits==8 and r==255 and g==b==a==0:format_info=('R8_UNORM',1)
    elif pixel_flags&0x40 and bits==32 and (r,g,b,a)==(255,65280,16711680,4278190080):format_info=('R8G8B8A8_UNORM',4)
    else:fail('Unsupported DDS pixel masks')
    fmt,bytes_per_pixel=format_info;row=width*bytes_per_pixel
    # DDS pitch is a row stride only when DDSD_PITCH is present.
    stride=pitch if flags&8 else row
    if stride<row or stride>row+65536:fail('Invalid DDS row pitch')
    size=stride*height
    if size>64*1024*1024 or len(data)!=offset+size:fail('DDS payload size does not match its image')
    pixels=b''.join(data[offset+y*stride:offset+y*stride+row] for y in range(height))
    return DdsSource(width,height,fmt,pixels)

def read(path):
    path=Path(path)
    if path.stat().st_size>64*1024*1024:raise ValueError(f'{path}: DDS exceeds asset size limit')
    return decode(path.read_bytes(),str(path))
