#!/usr/bin/env python3
"""Execute the complete registered original effect corpus; not a numerical reference gate."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
from effect_catalog import catalog

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--native-test',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--precision',choices=['fp32','auto','fp16'],default='fp32');args=parser.parse_args()
    binary=args.native_test.resolve();output=args.output.resolve();output.parent.mkdir(parents=True,exist_ok=True)
    cases=output.with_suffix('.cases.json');logpath=output.with_suffix('.log')
    effects={e['id']:e for e in catalog() if e['registered']}
    report={'passed':False,'state':'running','scope':'Actual GPU dispatch, output extent/opaque alpha/input responsiveness, repeat/resize and internal pass timing. Per-effect numerical fidelity is a separate gate.','native_test_sha256':hashlib.sha256(binary.read_bytes()).hexdigest(),'registered_effects':len(effects)}
    def save():output.write_text(json.dumps(report,indent=2)+'\n')
    save()
    with logpath.open('w') as log:
        command='--all-effects-smoke'+('' if args.precision=='fp32' else '-'+args.precision)
        run=subprocess.run([str(binary),command,str(cases)],stdout=log,stderr=subprocess.STDOUT,timeout=1200)
    text=logpath.read_text(errors='replace')
    if run.returncode or 'VUID-' in text:
        report['state']='failed';report['exit_code']=run.returncode;save();raise SystemExit(text[-5000:])
    rows=json.loads(cases.read_text());assert len(rows)==len(effects)*6
    assert 'ALL EFFECTS MIXED CHAIN PASS: 17 stages,' in text
    assert {r['effect'] for r in rows}==set(effects)
    for row in rows:
        effect=effects[row['effect']]
        assert row['passed'] and row['passes']==effect['passes'] and row['opaque_pixels']==row['pixels']
        if effect['scale']:assert row['output']==[v*effect['scale'] for v in row['input']]
    for identity in effects:
        group=[r for r in rows if r['effect']==identity];assert len(group)==6
        for dimensions in [(1,1),(7,5),(17,13)]:
            pair=[r for r in group if tuple(r['input'])==dimensions];assert len(pair)==2 and pair[0]['hash']!=pair[1]['hash']
    devices=sorted({line.split(' device=',1)[1] for line in text.splitlines() if line.startswith('BEGIN EFFECT ')})
    half_effects=sorted({r['effect'] for r in rows if r['fp16']})
    capable=sorted(e['id'] for e in effects.values() if e['fp16'])
    if args.precision=='fp16':assert half_effects==capable
    if args.precision=='auto':assert not half_effects or half_effects==capable
    if args.precision=='fp32':assert not half_effects
    report.update(passed=True,state='complete',precision=args.precision,fp16_effects=half_effects,cases=len(rows),devices=devices,internal_passes_in_corpus=sum(e['passes'] for e in effects.values()),mixed_chain_stages=17,mixed_chain_matches_separate_execution=True,case_file=cases.name,log_file=logpath.name,numerical_fidelity_verified_for_entire_corpus=False);save()
    print(f'PASS: {len(effects)} original effects / {len(rows)} GPU cases; repeat/resize, changed inputs, opaque output and real internal pass timing.')

if __name__=='__main__':main()
