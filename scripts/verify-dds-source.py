#!/usr/bin/env python3
"""Verify original SOURCE DDS assets and malformed/padded/DX10 decoding."""
from pathlib import Path
import argparse,hashlib,json,struct
from dds_source import decode,read
ROOT=Path(__file__).resolve().parents[1]
def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=Path,default=ROOT/'artifacts/desktop/dds-source.json');args=parser.parse_args()
    args.output.parent.mkdir(exist_ok=True,parents=True);args.output.write_text(json.dumps({'passed':False,'state':'running'})+'\n');cases=[]
    for p in sorted((ROOT/'src/Effects').rglob('*.dds')):
        data=p.read_bytes();asset=read(p)
        assert (asset.width,asset.height)==(struct.unpack_from('<I',data,16)[0],struct.unpack_from('<I',data,12)[0])
        assert asset.pixels==data[128:],p
        cases.append({'path':str(p.relative_to(ROOT)),'width':asset.width,'height':asset.height,'format':asset.format,'sha256':hashlib.sha256(data).hexdigest(),'payload_sha256':hashlib.sha256(asset.pixels).hexdigest()})
    original=(ROOT/'src/Effects/NIS/Coef_Scale.dds').read_bytes();count=0
    def rejected(data):
        nonlocal count
        try:decode(data,'fixture.dds')
        except ValueError as error:assert 'fixture.dds:' in str(error);count+=1;return
        raise AssertionError('Invalid DDS accepted')
    def change(offset,value,data=original):
        result=bytearray(data);struct.pack_into('<I',result,offset,value);return result
    for data in [b'',original[:127],b'BAD!'+original[4:],original[:-1],original+b'extra',change(4,123),change(76,31),change(16,0),change(12,0),change(16,16385),change(16,16384),change(28,2),change(24,2),change(112,512),change(84,0x31545844),change(20,1),change(108,0),change(8,0)]:rejected(data)
    padded=bytearray(original[:128]);struct.pack_into('<I',padded,20,24);payload=original[128:];padded+=b''.join(payload[y*16:y*16+16]+b'PADDING!' for y in range(64))
    assert decode(padded).pixels==payload
    dx10=change(84,struct.unpack('<I',b'DX10')[0],original[:128])+struct.pack('<5I',10,3,0,1,0)+payload
    assert decode(dx10).pixels==payload and decode(dx10).format=='R16G16B16A16_FLOAT'
    for offset,value in [(128,9999),(132,2),(136,4),(140,2),(140,0),(144,255)]:rejected(change(offset,value,dx10))
    rejected(dx10[:145])
    rgba=bytearray(original[:128]);struct.pack_into('<I',rgba,20,8);struct.pack_into('<7I',rgba,80,65,0,32,255,65280,16711680,4278190080);rgba+=bytes(2*64*4)
    assert decode(rgba).format=='R8G8B8A8_UNORM'
    rejected(change(92,0,rgba))
    report={'passed':True,'scope':'Uncompressed 2D SOURCE DDS build-time decoder; GPU upload tested separately','original_assets':cases,'negative_cases':count,'padded_pitch_passed':True,'dx10_passed':True,'rgba_masks_passed':True}
    args.output.write_text(json.dumps(report,indent=2)+'\n');print(f'PASS: {len(cases)} original DDS assets, {count} invalid cases, padded pitch/DX10/legacy/UNORM payloads.')
if __name__=='__main__':main()
