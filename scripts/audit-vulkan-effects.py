#!/usr/bin/env python3
"""Compile the original effect corpus through the actual Vulkan graph lowering.

An audit records unsupported effects rather than treating metadata parsing as
shader support. Success requires all selected effects to compile and validate;
it does not establish runtime registration or numerical correctness.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import time

from magpiefx import parse

ROOT=Path(__file__).resolve().parents[1]

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dxc',required=True,type=Path)
    parser.add_argument('--spirv-val',default='spirv-val')
    parser.add_argument('--effect',action='append',help='Original path without .hlsl; defaults to the entire corpus')
    parser.add_argument('--output',type=Path,default=ROOT/'artifacts/magpiefx/vulkan-audit')
    parser.add_argument('--timeout',type=int,default=120)
    args=parser.parse_args()
    dxc=args.dxc.resolve()
    validator=shutil.which(args.spirv_val)
    if not dxc.is_file() or not validator:parser.error('DXC and spirv-val must both be available')
    if not 1<=args.timeout<=600:parser.error('Timeout must be 1..600 seconds')
    names=args.effect or [str(p.relative_to(ROOT/'src/Effects').with_suffix('')).replace('\\','/') for p in sorted((ROOT/'src/Effects').rglob('*.hlsl'))]
    if len(set(names))!=len(names):parser.error('Duplicate effect paths')
    args.output=args.output.resolve();args.output.mkdir(parents=True,exist_ok=True)
    report={'passed':False,'state':'running','scope':'Actual DXC Vulkan 1.1 compute compilation and SPIR-V validation; no GPU execution or runtime registration implied',
            'runtime_tested':False,'dxc_sha256':digest(dxc),'source_sha256':{},'effects':[]}
    for p in sorted((ROOT/'src/Effects').rglob('*')):
        if p.is_file():report['source_sha256'][str(p.relative_to(ROOT))]=digest(p)
    for name in ['audit-vulkan-effects.py','compile-vulkan-shader.py','multipass_shaders.py','magpiefx.py','dds_source.py','effect_catalog.py']:
        report['source_sha256']['scripts/'+name]=digest(ROOT/'scripts'/name)
    def save():
        report['compiled_effects']=sum(e['passed'] for e in report['effects'])
        report['selected_effects']=len(names)
        report['compiled_passes']=sum(e['passes'] for e in report['effects'] if e['passed'])
        report['compiled_fp16_passes']=sum(e['fp16_passes'] for e in report['effects'] if e['passed'])
        report['compiled_kernels']=report['compiled_passes']+report['compiled_fp16_passes']
        path=args.output/'results.json';temporary=path.with_suffix('.tmp')
        temporary.write_text(json.dumps(report,indent=2)+'\n');temporary.replace(path)
    save()
    for index,name in enumerate(names):
        source=(ROOT/f'src/Effects/{name}.hlsl').resolve()
        if not source.is_relative_to((ROOT/'src/Effects').resolve()) or not source.is_file():parser.error('Invalid effect path: '+name)
        effect=parse(source.read_text(encoding='utf-8-sig'),name)
        folder=args.output/f'{index:03d}'
        # A fresh directory prevents stale kernels from making a partial compile
        # appear complete. Only this audit's numeric child is replaced.
        if folder.exists():shutil.rmtree(folder)
        folder.mkdir()
        item={'name':name,'passes':len(effect.passes),'fp16_passes':len(effect.passes) if 'FP16' in effect.capabilities else 0,'passed':False,'phase':'compile','log':str((folder/'compile.log').relative_to(args.output))}
        started=time.monotonic()
        command=[sys.executable,str(ROOT/'scripts/compile-vulkan-shader.py'),'--dxc',str(dxc),'--output',str(folder),'--effect',name]
        with (folder/'compile.log').open('w') as log:
            try:
                result=subprocess.run(command,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,timeout=args.timeout)
                item['exit_code']=result.returncode
                if result.returncode==0:
                    item['phase']='validate'
                    binaries=sorted(folder.glob('*.spv'))
                    stem=name.replace('/','_')
                    expected={stem+f'_pass{i+1}.spv' for i in range(len(effect.passes))}
                    if item['fp16_passes']:expected|={stem+f'_pass{i+1}_fp16.spv' for i in range(len(effect.passes))}
                    if {p.name for p in binaries}!=expected:raise ValueError('SPIR-V identities do not match the original pass graph and declared precision variants')
                    for binary in binaries:
                        subprocess.run([validator,'--target-env','vulkan1.1',str(binary)],stdout=log,stderr=subprocess.STDOUT,check=True,timeout=args.timeout)
                    item['spirv_sha256']={p.name:digest(p) for p in binaries}
                    item['passed']=True;item['phase']='complete'
            except (subprocess.TimeoutExpired,subprocess.CalledProcessError,ValueError) as error:
                item['error']=str(error)
                log.write('\nAUDIT: '+str(error)+'\n')
        if not item['passed']:
            lines=(folder/'compile.log').read_text(errors='replace').splitlines()
            item['diagnostic']='\n'.join(lines[-12:])
        item['seconds']=round(time.monotonic()-started,3)
        report['effects'].append(item);save()
        print(f'{index+1}/{len(names)} {name}: '+('compiled/validated' if item['passed'] else 'unsupported/failed'),flush=True)
    report['state']='complete';report['passed']=all(e['passed'] for e in report['effects']);save()
    print(f"Compiled and validated {report['compiled_effects']}/{len(names)} effects, {report['compiled_passes']} passes. Runtime execution is a separate gate.")
    return 0 if report['passed'] else 1

if __name__=='__main__':raise SystemExit(main())
