#!/usr/bin/env python3
"""Independent Jinc / bilateral mean references against registered native IDs 24/25."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import random
import subprocess
import tempfile

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('corpus_reference',ROOT/'scripts/verify-corpus-graphs.py')
reference=importlib.util.module_from_spec(spec);spec.loader.exec_module(reference)

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--native-test',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    binary=args.native_test.resolve();args.output=args.output.resolve();args.output.parent.mkdir(parents=True,exist_ok=True)
    report={'passed':False,'state':'running','native_test_sha256':hashlib.sha256(binary.read_bytes()).hexdigest(),'max_error_limit':1,'cases':[]}
    def save():args.output.write_text(json.dumps(report,indent=2)+'\n')
    save()
    with tempfile.TemporaryDirectory(prefix='magpie-native-filter-') as directory:
        folder=Path(directory);source=folder/'input.rgba';target=folder/'output.rgba';params=folder/'parameters.txt'
        for effect,name in [(24,'Jinc'),(25,'Anime4K/Anime4K_Denoise_Bilateral_Mean')]:
            variants=[[.5,.825,.5],[.5,.825,0],[.65,.9,1]] if effect==24 else [[.1],[.01],[1.5]]
            for w,h in [(1,1),(7,5),(17,13)]:
                outputs=[(w*2+1,h*2+1),(w,h),(w*2,h*2),(w*3,h*3),(w+2,h+1),(max(1,w//2),max(1,h//2))] if effect==24 else [(w,h)]
                for ow,oh in dict.fromkeys(outputs):
                    for pattern in ['constant','random']:
                        rng=random.Random(513);pixels=bytes(119 if i%4==3 else 73 if pattern=='constant' else rng.randrange(256) for i in range(w*h*4));source.write_bytes(pixels)
                        for variant,values in enumerate(variants):
                            params.write_text(' '.join(map(str,values)))
                            run=subprocess.run([str(binary),'--graph-filter-fixture',str(effect),str(w),str(h),str(ow),str(oh),str(source),str(target),str(params)],capture_output=True,text=True,timeout=60)
                            if run.returncode or 'VUID-' in run.stdout+run.stderr:raise RuntimeError(run.stdout+run.stderr)
                            expected=reference.reference(name,pixels,w,h,ow,oh,values);actual=target.read_bytes();assert len(actual)==len(expected)
                            error=max(abs(a-b) for a,b in zip(actual,expected));case={'effect':effect,'input':[w,h],'output':[ow,oh],'pattern':pattern,'variant':variant,'max_channel_error':error,'passed':error<=1,'gpu_log':run.stdout.strip()};report['cases'].append(case);save()
                            if error>1:raise AssertionError(f'{name} {w}x{h}->{ow}x{oh} variant={variant}: error={error}')
        report['state']='complete';report['passed']=all(c['passed'] for c in report['cases']);save()
    print(f'PASS: {len(report["cases"])} registered Jinc/bilateral mean CPU/Vulkan cases; max channel error={max(c["max_channel_error"] for c in report["cases"])}.')

if __name__=='__main__':main()
