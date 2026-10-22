#!/usr/bin/env python3
"""Prepare pinned static MoltenVK, run native macOS gates and make a local test app."""
import argparse
import hashlib
import os
from pathlib import Path
import platform
import plistlib
import shutil
import subprocess
import sys
import tarfile
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
VERSION = '1.4.2'
SHA256 = 'f95765a6229cb7b915990a2890ce12ebe36a730b021545d3d52ae69ce4c4024e'
URL = f'https://github.com/KhronosGroup/MoltenVK/releases/download/v{VERSION}/MoltenVK-macos.tar'
GLM_VERSION = '1.0.1'
GLM_SHA256 = '9f3174561fd26904b23f0db5e560971cbf9b3cbda0b280f04d5c379d03bf234c'


def prepare_glm():
    """Header-only dependency used by the independent SMAA CPU reference."""
    destination = ROOT / '.tools/glm'
    destination.mkdir(parents=True, exist_ok=True)
    package = destination / f'glm-{GLM_VERSION}.tar.gz'
    if not package.exists():
        temporary = package.with_suffix('.download')
        url = f'https://github.com/g-truc/glm/archive/refs/tags/{GLM_VERSION}.tar.gz'
        print('Downloading pinned GLM ' + GLM_VERSION, flush=True)
        request = urllib.request.Request(url, headers={'User-Agent': 'Magpie-macOS-build'})
        with urllib.request.urlopen(request, timeout=60) as source, temporary.open('wb') as target:
            shutil.copyfileobj(source, target)
        os.replace(temporary, package)
    if hashlib.sha256(package.read_bytes()).hexdigest() != GLM_SHA256:
        raise SystemExit('GLM archive SHA256 mismatch: ' + str(package))
    with tarfile.open(package) as bundle:
        bundle.extractall(destination, filter='data')
    headers = destination / f'glm-{GLM_VERSION}'
    if not (headers / 'glm/glm.hpp').is_file():
        raise SystemExit('The pinned GLM archive is missing its headers.')
    print('Verified GLM headers: ' + str(headers), flush=True)
    return headers


def prepare(archive=None):
    destination = ROOT / '.tools/moltenvk' / VERSION
    destination.mkdir(parents=True, exist_ok=True)
    package = Path(archive).resolve() if archive else destination / 'MoltenVK-macos.tar'
    if not package.exists():
        temporary = package.with_suffix('.download')
        print('Downloading pinned public-API MoltenVK ' + VERSION, flush=True)
        request = urllib.request.Request(URL, headers={'User-Agent': 'Magpie-macOS-build'})
        with urllib.request.urlopen(request, timeout=60) as source, temporary.open('wb') as target:
            shutil.copyfileobj(source, target)
        os.replace(temporary, package)
    if hashlib.sha256(package.read_bytes()).hexdigest() != SHA256:
        raise SystemExit('MoltenVK archive SHA256 mismatch: ' + str(package))
    with tarfile.open(package) as bundle:
        bundle.extractall(destination, filter='data')
    base = destination / 'MoltenVK/MoltenVK'
    library = base / 'static/MoltenVK.xcframework/macos-arm64_x86_64/libMoltenVK.a'
    headers = base / 'include'
    if not library.is_file() or not (headers / 'vulkan/vulkan.h').is_file():
        raise SystemExit('The pinned archive is missing its static library or Vulkan headers.')
    print('Verified static MoltenVK: ' + str(library), flush=True)
    return library, headers


def package_app(rid):
    executable = ROOT / 'artifacts/desktop' / rid / 'Magpie.Desktop'
    app = executable.parent / 'Magpie.app'
    contents = app / 'Contents'
    binary = contents / 'MacOS/Magpie.Desktop'
    binary.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(executable, binary)
    info = {
        'CFBundleIdentifier': 'io.github.greepar.magpie',
        'CFBundleName': 'Magpie', 'CFBundleDisplayName': 'Magpie',
        'CFBundleExecutable': 'Magpie.Desktop', 'CFBundlePackageType': 'APPL',
        'CFBundleVersion': '0.1.0', 'CFBundleShortVersionString': '0.1.0',
        'LSMinimumSystemVersion': '14.0', 'NSHighResolutionCapable': True,
        'NSPrincipalClass': 'NSApplication',
    }
    with (contents / 'Info.plist').open('wb') as target:
        plistlib.dump(info, target)
    # Local testing identity only. Distribution still needs Developer ID/notarization.
    subprocess.run(['codesign', '--force', '--sign', '-', '--timestamp=none', str(app)], check=True)
    subprocess.run(['codesign', '--verify', '--strict', str(app)], check=True)
    print('\nLocal test app: ' + str(app))
    print('Open it yourself with: open ' + str(app))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', type=Path, help='Use an already downloaded official archive.')
    parser.add_argument('--prepare-only', action='store_true', help='Verify/extract dependencies without building or opening UI.')
    args = parser.parse_args()
    if not args.prepare_only:
        if sys.platform != 'darwin':
            raise SystemExit('Run macOS build/runtime checks on a Mac; cross-OS checks are not substituted.')
        if tuple(map(int, platform.mac_ver()[0].split('.')[:1])) < (14,):
            raise SystemExit('ScreenCaptureKit system selection requires macOS 14 or later.')
        for tool in ('dotnet', 'cmake', 'clang', 'codesign', 'xcrun'):
            if not shutil.which(tool):
                raise SystemExit('Missing build tool: ' + tool)
    library, headers = prepare(args.archive)
    glm_headers = prepare_glm()
    if args.prepare_only:
        return
    architecture = {'arm64': 'arm64', 'x86_64': 'x64'}.get(platform.machine())
    if not architecture:
        raise SystemExit('Unsupported Mac architecture: ' + platform.machine())
    rid = 'osx-' + architecture
    env = dict(os.environ, MAGPIE_MOLTENVK_LIBRARY=str(library), MAGPIE_VULKAN_INCLUDE=str(headers),
               MAGPIE_GLM_INCLUDE_DIR=str(glm_headers))
    # Resolve the SDK explicitly: an unqualified xcrun/clang can select the CLT
    # SDK even when xcode-select points at a different Xcode toolchain.
    if not env.get('SDKROOT'):
        env['SDKROOT'] = subprocess.check_output(
            ['xcrun', '--sdk', 'macosx', '--show-sdk-path'], text=True).strip()
    # verify-platform strips display variables and never opens a capture picker.
    subprocess.run([sys.executable, str(ROOT / 'scripts/verify-platform.py'), '--rid', rid], cwd=ROOT, env=env, check=True)
    package_app(rid)


if __name__ == '__main__':
    main()
