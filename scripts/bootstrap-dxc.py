#!/usr/bin/env python3
"""Install the pinned official DXC build locally; no system-wide changes."""
import hashlib
from pathlib import Path
import tarfile
import tempfile
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
URL = 'https://github.com/microsoft/DirectXShaderCompiler/releases/download/v1.9.2609/linux_dxc_2026_09_28.x86_x64.tar.gz'
SHA256 = '96faadc7f5c282d2ffda49804beb4c3ee38127bc252b723234e3c5cdf7aa39a1'
destination = ROOT / '.tools/dxc'
with tempfile.TemporaryDirectory(prefix='magpie-dxc-') as temporary:
    archive = Path(temporary) / 'dxc.tar.gz'
    urllib.request.urlretrieve(URL, archive)
    if hashlib.sha256(archive.read_bytes()).hexdigest() != SHA256:
        raise SystemExit('DXC archive checksum mismatch.')
    destination.mkdir(parents=True, exist_ok=True)
    with tarfile.open(archive) as bundle:
        bundle.extractall(destination, filter='data')
print(destination / 'bin/dxc')
