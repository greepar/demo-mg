#!/usr/bin/env python3
"""Validate actual native Float16 SPIR-V for every original FP16-capable effect."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import struct
import subprocess
from effect_catalog import registry,ROOT
from magpiefx import parse


def inspect(data):
    words=struct.unpack('<'+'I'*(len(data)//4),data)
    assert words[0]==0x07230203
    pos=5;instructions=[];types=set();capabilities=set();rte16=False
    while pos<len(words):
        count,opcode=words[pos]>>16,words[pos]&65535
        assert count and pos+count<=len(words)
        args=words[pos+1:pos+count];instructions.append((opcode,args));pos+=count
        if opcode==17:capabilities.add(args[0])
        if opcode==16 and len(args)==3 and args[1:]==(4462,16):rte16=True
        if opcode==22 and args[1]==16:types.add(args[0])
        if opcode in (23,24) and args[1] in types:types.add(args[0])
    arithmetic=sum(bool(args and args[0] in types and op in {12,127,129,131,133,136,142,143,144,145,146,148}) for op,args in instructions)
    precise={args[0] for op,args in instructions if op==71 and len(args)>=2 and args[1]==42}
    noncontracted=sum(bool(len(args)>1 and args[0] in types and args[1] in precise) for op,args in instructions if op in {12,129,131,133,136,142,143,144,145,146,148})
    conversions=sum(op==115 for op,args in instructions) # OpFConvert, including the source NIS FP16 coefficient path.
    return dict(float16_capability=9 in capabilities,round_to_even_16=rte16,half_types=len(types),half_arithmetic_instructions=arithmetic,noncontracted_half_arithmetic_instructions=noncontracted,precision_conversions=conversions)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--generated',type=Path,required=True)
    parser.add_argument('--spirv-val',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();folder=args.generated.resolve();validator=args.spirv_val.resolve();cases=[]
    for row in registry():
        effect=parse((ROOT/'src/Effects'/str(row['path']+'.hlsl')).read_text(encoding='utf-8-sig'),row['path'])
        if 'FP16' not in effect.capabilities:continue
        stems=[row['path'].replace('/','_')+(f'_pass{i+1}' if row['backend']=='graph' else '') for i in range(len(effect.passes))]
        for stem in stems:
            normal=folder/(stem+'.spv');half=folder/(stem+'_fp16.spv')
            stats=inspect(half.read_bytes());assert stats['float16_capability'] and stats['round_to_even_16'] and stats['half_types'] and (stats['half_arithmetic_instructions'] or stats['precision_conversions']),stem
            assert not inspect(normal.read_bytes())['float16_capability'],stem
            cases.append(dict(effect=row['path'],kernel=half.name,sha256=hashlib.sha256(half.read_bytes()).hexdigest(),**stats))
    assert len(cases)==len(list(folder.glob('*_fp16.spv')))
    files=list(folder.glob('*.spv'))
    def validate(path):
        run=subprocess.run([str(validator),'--target-env','vulkan1.1',str(path)],capture_output=True,text=True)
        if run.returncode:raise RuntimeError(str(path)+': '+run.stdout+run.stderr)
    with ThreadPoolExecutor(max_workers=4) as pool:list(pool.map(validate,files))
    report=dict(passed=True,capable_effects=len({c['effect'] for c in cases}),half_kernels=len(cases),validated_total_kernels=len(files),effects_with_native_half_arithmetic=len({c['effect'] for c in cases if c['half_arithmetic_instructions']}),half_arithmetic_instructions=sum(c['half_arithmetic_instructions'] for c in cases),noncontracted_half_arithmetic_instructions=sum(c['noncontracted_half_arithmetic_instructions'] for c in cases),cases=cases)
    args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(report,indent=2)+'\n')
    print(f'PASS: {report["capable_effects"]} FP16 source branches, {len(cases)} native Float16 kernels, {report["half_arithmetic_instructions"]} half arithmetic instructions; all {len(files)} SPIR-V kernels validated.')

if __name__=='__main__':main()
