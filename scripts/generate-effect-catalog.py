#!/usr/bin/env python3
"""Generate/check managed original-effect metadata, optionally compare native shader tables."""
import argparse
import json
from pathlib import Path
import struct
import subprocess
import copy
import tempfile
import effect_catalog
from effect_catalog import ROOT,catalog

TARGET=ROOT/'src/Magpie.Desktop/Core/GraphCatalog.Generated.cs'
def quoted(value):return json.dumps(value,ensure_ascii=True)
def shortest(value):
    # Metadata numbers are binary32. Emit the shortest decimal that roundtrips
    # to that value, instead of turning UI increments such as .01 into long
    # decimal tails from the float32 representation.
    for precision in range(1,10):
        text=format(float(value),f'.{precision}g')
        if struct.pack('<f',float(text))==struct.pack('<f',value):return text
    raise ValueError('Unable to represent binary32 metadata')
def number(value):return shortest(value)+'f'

def generate(effects):
    lines=['// Generated from original MagpieFX metadata. Use scripts/generate-effect-catalog.py.',
           'namespace Magpie.Desktop.Core;','internal static partial class GraphCatalog','{',
           '    internal static readonly GraphDescriptor[] All = [']
    for e in effects:
        params=','.join('new('+','.join([quoted(p['name']),quoted(p['label']),number(p['default']),number(p['minimum']),number(p['maximum']),shortest(p['step'])+'m',str(p['integer']).lower()])+')' for p in e['parameters'])
        textures=','.join('new('+','.join(map(str,[*t['width'],*t['height'],t['bytes']]))+')' for t in e['textures'])
        lines.append('        new('+','.join([str(e['id']),quoted(e['path']),str(e['registered']).lower(),str(e['backend']=='graph').lower(),str(e['fp16']).lower(),str(e['scale']),str(e['passes']),'['+params+']','['+textures+']'])+'),')
    return '\n'.join(lines+['    ];','}'])+'\n'

def native_check(binary,effects):
    run=subprocess.run([str(binary.resolve()),'--effect-metadata'],capture_output=True,text=True,check=True,timeout=30)
    native=json.loads(run.stdout);expected={e['id']:e for e in effects if e['registered'] and e['backend']=='graph'}
    assert {e['id'] for e in native}==set(expected),'Native/managed registered graph IDs differ'
    formats={'R16G16B16A16_FLOAT':0,'R8G8B8A8_UNORM':1,'R16_FLOAT':2,'R16G16_FLOAT':3,'R8_UNORM':4,'R8G8_UNORM':5}
    f32=lambda x:struct.pack('<f',x)
    for actual in native:
        e=expected[actual['id']];assert actual['fp16']==e['fp16'],e['path']
        assert actual['scale']==e['scale'] and actual['passes']==e['passes'],e['path']
        assert len(actual['textures'])==len(e['textures']),e['path']
        for a,t in zip(actual['textures'],e['textures']):assert a==[formats[t['format']],*t['width'],*t['height'],t['bytes']],e['path']
        assert len(actual['parameters'])==len(e['parameters']),e['path']
        for a,p in zip(actual['parameters'],e['parameters']):
            assert all(f32(a[i])==f32(p[k]) for i,k in enumerate(['minimum','maximum','default'])),(e['path'],p['name'])
            assert a[3]==p['integer'],(e['path'],p['name'])
    return len(native)

def self_test(effects,binary):
    count=0;original=effect_catalog.REGISTRY
    def reject(call):
        nonlocal count
        try:call()
        except (ValueError,AssertionError):count+=1;return
        raise AssertionError('Corrupt effect metadata was accepted')
    data=json.loads(original.read_text())
    with tempfile.TemporaryDirectory(prefix='magpie-catalog-') as folder:
        path=Path(folder)/'effects.json'
        changes=[lambda d:d.update(version=2),lambda d:d.update(version=True),
                 lambda d:d['effects'][0].update(id=6),lambda d:d['effects'][0].update(id=False),
                 lambda d:d['effects'][0].update(id=0x100000000),lambda d:d['effects'][1].update(id=0),
                 lambda d:d['effects'][1].update(path=d['effects'][0]['path']),
                 lambda d:d['effects'][0].update(registered=1),lambda d:d['effects'][0].update(registered=False),
                 lambda d:d['effects'][0].update(order=-1),lambda d:d['effects'].pop()]
        try:
            effect_catalog.REGISTRY=path
            for change in changes:
                altered=copy.deepcopy(data);change(altered);path.write_text(json.dumps(altered));reject(effect_catalog.registry)
        finally:effect_catalog.REGISTRY=original
    if binary:
        target=next(e for e in effects if e['registered'] and e['textures'])['id']
        parameter_target=next(e for e in effects if e['registered'] and e['backend']=='graph' and e['parameters'])['id']
        mutations=[(target,lambda e:e.update(fp16=not e['fp16'])),(target,lambda e:e.update(scale=e['scale']+1)),(target,lambda e:e.update(passes=e['passes']+1)),
                   (target,lambda e:e['textures'][0].update(bytes=1)),
                   (parameter_target,lambda e:e['parameters'][0].update(minimum=-123)),
                   (parameter_target,lambda e:e['parameters'][0].update(integer=not e['parameters'][0]['integer']))]
        for identity,change in mutations:
            altered=copy.deepcopy(effects);change(next(e for e in altered if e['id']==identity));reject(lambda:native_check(binary,altered))
    return count

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--write',action='store_true');parser.add_argument('--native-test',type=Path);parser.add_argument('--self-test',action='store_true');parser.add_argument('--output',type=Path);args=parser.parse_args()
    effects=catalog();source=generate(effects)
    if args.write:TARGET.write_text(source,encoding='utf-8')
    elif not TARGET.exists() or TARGET.read_text(encoding='utf-8')!=source:raise SystemExit('Managed effect metadata is stale. Run generate-effect-catalog.py --write.')
    compared=native_check(args.native_test,effects) if args.native_test else 0
    negative_cases=self_test(effects,args.native_test) if args.self_test else 0
    report={'passed':True,'metadata_effects':len(effects),'registered_effects':sum(e['registered'] for e in effects),'native_graphs_compared':compared,'negative_cases':negative_cases,'effect_ids':[e['id'] for e in effects],'metadata':effects}
    if args.output:args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(report,indent=2)+'\n')
    print(f'PASS: {len(effects)} stable effect identities/metadata; {compared} actual native graph tables compared; {negative_cases} corrupt cases rejected.')

if __name__=='__main__':main()
