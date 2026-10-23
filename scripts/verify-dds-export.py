#!/usr/bin/env python3
"""Decode AOT-generated raw DDS with the independent existing SOURCE decoder; never opens UI."""
import argparse,hashlib,json,subprocess,sys,tempfile
from pathlib import Path
from dds_source import read
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--app',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
a.output.parent.mkdir(parents=True,exist_ok=True)
formats=['R16G16B16A16_FLOAT','R8G8B8A8_UNORM','R16_FLOAT','R16G16_FLOAT','R8_UNORM','R8G8_UNORM'];sizes=[8,4,2,4,1,2];cases=[]
with tempfile.TemporaryDirectory(prefix='magpie-dds-export-') as folder:
    result=subprocess.run([str(a.app.resolve()),'--dds-export-probe',folder],cwd=ROOT,capture_output=True,text=True,encoding='utf-8')
    if result.returncode:raise SystemExit(result.stdout+result.stderr)
    for index,format in enumerate(formats):
        path=Path(folder)/('format-'+str(index)+'.dds');asset=read(path)
        expected=bytes((i*37+index*11)%256 for i in range(6*sizes[index]))
        assert (asset.width,asset.height,asset.format)==(3,2,format),(index,asset)
        assert asset.pixels==expected,index
        cases.append({'format':format,'file_bytes':path.stat().st_size,'payload_sha256':hashlib.sha256(expected).hexdigest()})
a.output.write_text(json.dumps({'passed':True,'ui_opened':False,'app_sha256':hashlib.sha256(a.app.read_bytes()).hexdigest(),'cases':cases},indent=2)+'\n')
print('PASS: six AOT-produced DDS formats decoded independently with exact original raw bytes; no GPU/UI.')
