#!/usr/bin/env python3
"""Fail if checked-in SPIR-V no longer matches its original HLSL or adapter."""
import hashlib
import json
from pathlib import Path
import argparse
import gzip
import os

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--write-manifest',action='store_true')
parser.add_argument('--extract-header',type=Path,help='Write verified embedded kernels into a native build directory.')
args=parser.parse_args()
manifest=ROOT/'src/Magpie.Native/shaders/precompiled.json'
header='src/Magpie.Native/shaders/kernel.precompiled.h'
archive=header+'.gz'
if args.write_manifest:
    # Deterministic archive: GitHub rejects the uncompressed 128 MiB header.
    with (ROOT/header).open('rb') as source, (ROOT/archive).open('wb') as destination:
        with gzip.GzipFile(filename='',mode='wb',fileobj=destination,mtime=0,compresslevel=9) as compressed:
            import shutil
            shutil.copyfileobj(source,compressed)
paths=['src/Magpie.Native/effects.json','scripts/effect_catalog.py','scripts/dds_source.py','scripts/magpiefx.py','scripts/compile-vulkan-shader.py','scripts/multipass_shaders.py',
       *sorted(p.relative_to(ROOT).as_posix() for p in (ROOT/'src/Effects').rglob('*') if p.is_file() and p.suffix in ('.hlsl','.hlsli','.dds')),
       archive]
actual={p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in paths}
with gzip.open(ROOT/archive,'rb') as compressed: kernels=compressed.read()
actual[header]=hashlib.sha256(kernels).hexdigest()
if args.write_manifest:manifest.write_text(json.dumps(actual,indent=2)+'\n')
else:
    expected=json.loads(manifest.read_text())
    if actual!=expected:raise SystemExit('Embedded shader inputs changed. Regenerate kernels with DXC and update precompiled.json.')
    print('PASS: embedded SPIR-V source/adapter/header digests.')
if args.extract_header:
    target=args.extract_header
    if not target.is_file() or hashlib.sha256(target.read_bytes()).hexdigest()!=actual[header]:
        target.parent.mkdir(parents=True,exist_ok=True)
        temporary=target.with_name(target.name+'.tmp')
        temporary.write_bytes(kernels)
        os.replace(temporary,target)
    print('Verified kernels extracted to '+str(target))
