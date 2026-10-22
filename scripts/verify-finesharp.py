#!/usr/bin/env python3
"""Check all five FineSharp stages against independent scalar/half-texture equations."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import subprocess
import tempfile
from effect_catalog import registry
from finesharp_reference import reference


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--native-test',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();binary=args.native_test.resolve();args.output.parent.mkdir(parents=True,exist_ok=True)
    identity=next(r['id'] for r in registry() if r['path']=='Sharpen/FineSharp')
    report=dict(passed=False,state='running',native_test_sha256=hashlib.sha256(binary.read_bytes()).hexdigest(),max_error_limit=1,cases=[])
    def save():args.output.write_text(json.dumps(report,indent=2)+'\n')
    save()
    with tempfile.TemporaryDirectory(prefix='magpie-finesharp-') as directory:
        folder=Path(directory);source,target,params=[folder/n for n in ('input.rgba','output.rgba','parameters.txt')]
        variants=[[2,.9,.19,.25],[0,0,0,0],[8,2,1,1],[2,0,0,0],[0,2,1,0],[4,1.19,.249,.5]]
        for w,h in [(1,1),(7,5),(17,13),(33,25),(3,576),(3,577)]:
            for pattern in ['black','white','constant','random','edge']:
                rng=random.Random(853)
                pixels=bytes(119 if c==3 else 0 if pattern=='black' else 255 if pattern=='white' else 73 if pattern=='constant' else rng.randrange(256) if pattern=='random' else (255 if (x>=w//2) != (y>=h//2) else 0) for y in range(h) for x in range(w) for c in range(4))
                source.write_bytes(pixels)
                for values in variants:
                    params.write_text(' '.join(map(str,values)))
                    run=subprocess.run([str(binary),'--original-effect-fixture',str(identity),str(w),str(h),str(w),str(h),str(source),str(target),str(params)],capture_output=True,text=True,timeout=60)
                    if run.returncode or 'VUID-' in run.stdout+run.stderr:raise RuntimeError(run.stdout+run.stderr)
                    actual=target.read_bytes();expected=reference(pixels,w,h,values);assert len(actual)==len(expected)
                    error=max(abs(a-b) for a,b in zip(actual,expected))
                    report['cases'].append(dict(input=[w,h],pattern=pattern,parameters=values,max_channel_error=error,passed=error<=1,gpu_log=run.stdout.strip()))
                    if error>1:
                        (args.output.parent/'failed-input.rgba').write_bytes(pixels)
                        (args.output.parent/'failed-actual.rgba').write_bytes(actual)
                        (args.output.parent/'failed-expected.rgba').write_bytes(expected)
                        report['state']='failed';save();raise AssertionError(f'FineSharp {w}x{h} {pattern} {values}: CPU/GPU error={error}')
            save()
    report.update(passed=True,state='complete');save()
    print(f'PASS: {len(report["cases"])} independent five-pass FineSharp CPU/Vulkan cases; max error={max(c["max_channel_error"] for c in report["cases"])}.')

if __name__=='__main__':main()
