#!/usr/bin/env python3
"""Independent original AdaptiveSharpen nonlinear edge/limiting reference."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import subprocess
import tempfile
from effect_catalog import registry
from adaptive_sharpen_reference import reference


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--native-test',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();binary=args.native_test.resolve();args.output.parent.mkdir(parents=True,exist_ok=True)
    identity=next(r['id'] for r in registry() if r['path']=='Sharpen/AdaptiveSharpen')
    report=dict(passed=False,state='running',native_test_sha256=hashlib.sha256(binary.read_bytes()).hexdigest(),max_error_limit=1,cases=[])
    def save():args.output.write_text(json.dumps(report,indent=2)+'\n')
    save()
    with tempfile.TemporaryDirectory(prefix='magpie-adaptive-sharpen-') as directory:
        folder=Path(directory);source,target,params=[folder/n for n in ('input.rgba','output.rgba','parameters.txt')]
        for w,h in [(1,1),(7,5),(17,13),(33,25)]:
            for pattern in ['black','white','constant','random','edge','impulse']:
                rng=random.Random(151)
                pixels=bytes(119 if c==3 else 0 if pattern=='black' else 255 if pattern=='white' else 73 if pattern=='constant' else rng.randrange(256) if pattern=='random' else 255 if (pattern=='edge' and ((x>=w//2)!=(y>=h//2))) or (pattern=='impulse' and x==w//2 and y==h//2) else 0 for y in range(h) for x in range(w) for c in range(4))
                source.write_bytes(pixels)
                for strength in [0,.3,.8,2]:
                    params.write_text(str(strength))
                    run=subprocess.run([str(binary),'--original-effect-fixture',str(identity),str(w),str(h),str(w),str(h),str(source),str(target),str(params)],capture_output=True,text=True,timeout=60)
                    if run.returncode or 'VUID-' in run.stdout+run.stderr:raise RuntimeError(run.stdout+run.stderr)
                    actual=target.read_bytes();expected=reference(pixels,w,h,strength);assert len(actual)==len(expected)
                    error=max(abs(a-b) for a,b in zip(actual,expected))
                    report['cases'].append(dict(input=[w,h],pattern=pattern,strength=strength,max_channel_error=error,passed=error<=1,gpu_log=run.stdout.strip()))
                    if error>1:
                        report['state']='failed';save();raise AssertionError(f'AdaptiveSharpen {w}x{h} {pattern} strength={strength}: CPU/GPU error={error}')
            save()
    report.update(passed=True,state='complete');save()
    print(f'PASS: {len(report["cases"])} independent AdaptiveSharpen CPU/Vulkan cases; max error={max(c["max_channel_error"] for c in report["cases"])}.')

if __name__=='__main__':main()
