#!/usr/bin/env python3
"""Build the native library on the target host (NativeAOT cross-OS publishing is unsupported)."""
from pathlib import Path
import argparse
import os
import platform
import shutil
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--rid',required=True)
parser.add_argument('--configuration',choices=['Debug','Release'],required=True)
parser.add_argument('--build-dir',type=Path,required=True)
parser.add_argument('--dxc',default='')
args=parser.parse_args()
host={'linux':'linux','win32':'win','darwin':'osx'}.get(sys.platform)
arch={'x86_64':'x64','AMD64':'x64','arm64':'arm64','aarch64':'arm64'}.get(platform.machine())
if args.rid != f'{host}-{arch}':raise SystemExit(f'Build {args.rid} on its matching host; current host is {host}-{arch}.')
command=['cmake','-S',str(ROOT/'src/Magpie.Native'),'-B',str(args.build_dir.resolve()),'-DCMAKE_BUILD_TYPE='+args.configuration]
if host=='win':command += ['-A','ARM64' if arch=='arm64' else 'x64']
elif shutil.which('ninja'):command += ['-G','Ninja']
if args.dxc and Path(args.dxc).exists():command += ['-DDXC_EXECUTABLE='+str(Path(args.dxc).resolve())]
else:command += ['-DDXC_EXECUTABLE=']
if host=='osx':
    molten=os.environ.get('MAGPIE_MOLTENVK_LIBRARY','')
    if not molten or not Path(molten).is_file() or Path(molten).suffix!='.a':
        raise SystemExit('macOS requires MAGPIE_MOLTENVK_LIBRARY pointing to a static libMoltenVK.a; dynamic fallback is not allowed.')
    command += ['-DVulkan_LIBRARY='+str(Path(molten).resolve()),'-DCMAKE_OSX_DEPLOYMENT_TARGET=14.0','-DCMAKE_OSX_ARCHITECTURES='+('arm64' if arch=='arm64' else 'x86_64')]
    include=os.environ.get('MAGPIE_VULKAN_INCLUDE') or (str(Path(os.environ['VULKAN_SDK'])/'include') if os.environ.get('VULKAN_SDK') else '')
    if include:command += ['-DVulkan_INCLUDE_DIR='+include]
subprocess.run(command,check=True)
subprocess.run(['cmake','--build',str(args.build_dir.resolve()),'--config',args.configuration],check=True)
