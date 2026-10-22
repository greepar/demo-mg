#!/usr/bin/env python3
"""Optional device FP16: actual automatic dispatch and independent binary16 CNN references."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[1]

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--native-test',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    args.output=args.output.resolve();args.output.parent.mkdir(parents=True,exist_ok=True);binary=args.native_test.resolve()
    args.output.write_text(json.dumps(dict(passed=False,state='running'))+'\n')
    try:
        info=subprocess.run([str(binary),'--precision-info'],check=True,capture_output=True,text=True,timeout=60)
        if 'VUID-' in info.stdout+info.stderr:raise RuntimeError(info.stdout+info.stderr)
        device=json.loads(info.stdout)
        execution=args.output.with_name(args.output.stem+'-execution.json')
        subprocess.run([sys.executable,str(ROOT/'scripts/verify-all-effects.py'),'--native-test',str(binary),'--precision','auto','--output',str(execution)],check=True,timeout=1260)
        numerical=args.output.with_name(args.output.stem+'-anime4k.json')
        if device['supported']:
            subprocess.run([sys.executable,str(ROOT/'scripts/verify-anime4k.py'),'--native-test',str(binary),'--fp16','--output',str(numerical)],check=True,timeout=180)
        report=dict(passed=True,state='complete',device=device,native_fp16_executed=device['supported'],fp16_independent_numerical_tested=device['supported'],automatic_execution=str(execution),fp16_numerical_reference=str(numerical) if device['supported'] else None,scope='Device capability, FP32/automatic/forced policy, all effects in automatic mode and six-model binary16 CNN reference when supported; not full corpus numerical parity.')
        args.output.write_text(json.dumps(report,indent=2)+'\n')
        print('PASS: automatic FP16 runtime and independent references.' if device['supported'] else 'PASS: actual unsupported-device FP32 fallback and forced-FP16 rejection; FP16 execution unavailable on this device.')
    except Exception as failure:
        args.output.write_text(json.dumps(dict(passed=False,state='failed',error=str(failure)),indent=2)+'\n')
        raise

if __name__=='__main__':main()
