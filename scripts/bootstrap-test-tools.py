#!/usr/bin/env python3
"""Extract Ubuntu test tools locally, without installing packages or changing the desktop."""
from pathlib import Path
import argparse
import subprocess

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--validation',action='store_true',help='Also fetch Vulkan validation layers')
args=parser.parse_args()
groups={'weston':['weston','libweston-13-0']}
if args.validation:groups['validation']=['vulkan-validationlayers']
for name,packages in groups.items():
    base=ROOT/'artifacts/test-tools'/name
    downloads=base/'packages';downloads.mkdir(parents=True,exist_ok=True)
    extract=base/'root';extract.mkdir(parents=True,exist_ok=True)
    subprocess.run(['apt-get','download',*packages],cwd=downloads,check=True)
    for archive in downloads.glob('*.deb'):subprocess.run(['dpkg-deb','-x',str(archive),str(extract)],check=True)
    print(extract)
