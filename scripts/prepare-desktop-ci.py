#!/usr/bin/env python3
"""Prepare pinned native CI dependencies; never install a driver or open a desktop UI."""
import hashlib
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
BASE=ROOT/'.tools/desktop-ci'
TAG='vulkan-sdk-1.3.296.0'
PACKAGES={
    'Vulkan-Headers':'1e872a0be3890784bbe68dcd89b7e017fed77ba95820841848718c98bda6dc33',
    'Vulkan-Loader':'924b439421c30513f59dfd360cb1309639c1522de28608eb64c9d79581e44c72',
}

def windows_sources():
    BASE.mkdir(parents=True,exist_ok=True)
    sources={}
    for name,digest in PACKAGES.items():
        package=BASE/(name+'.tar.gz')
        if not package.exists():
            temporary=package.with_suffix('.download')
            request=urllib.request.Request(f'https://codeload.github.com/KhronosGroup/{name}/tar.gz/refs/tags/{TAG}',headers={'User-Agent':'Magpie-native-CI'})
            with urllib.request.urlopen(request,timeout=60) as source,temporary.open('wb') as target:shutil.copyfileobj(source,target)
            os.replace(temporary,package)
        if hashlib.sha256(package.read_bytes()).hexdigest()!=digest:raise RuntimeError('Dependency SHA256 mismatch: '+name)
        with tarfile.open(package) as bundle:bundle.extractall(BASE,filter='data')
        source=BASE/(name+'-'+TAG)
        if not (source/'CMakeLists.txt').is_file():raise RuntimeError('Dependency archive has no expected project: '+name)
        sources[name]=source
        print('Verified official source: '+name+' '+TAG,flush=True)
    return sources

def prepare_windows():
    sources=windows_sources();sdk=BASE/'vulkan-sdk'
    for name in ('Vulkan-Headers','Vulkan-Loader'):
        build=BASE/(name+'-build')
        configure=['cmake','-S',str(sources[name]),'-B',str(build),'-A','x64','-DCMAKE_INSTALL_PREFIX='+str(sdk)]
        if name=='Vulkan-Loader':configure+=['-DVULKAN_HEADERS_INSTALL_DIR='+str(sdk),'-DCMAKE_PREFIX_PATH='+str(sdk),'-DBUILD_TESTS=OFF','-DUPDATE_DEPS=OFF']
        subprocess.run(configure,check=True)
        subprocess.run(['cmake','--build',str(build),'--config','Release','--target','install','--parallel','4'],check=True)
    for item in ('include/vulkan/vulkan.h','lib/vulkan-1.lib','bin/vulkan-1.dll'):
        if not (sdk/item).is_file():raise RuntimeError('Missing prepared Vulkan SDK component: '+item)
    return {'VULKAN_SDK':str(sdk)},sdk/'bin'

def prepare_macos():
    spec=importlib.util.spec_from_file_location('magpie_macos_dependencies',ROOT/'scripts/test-macos.py')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    library,headers=module.prepare();glm=module.prepare_glm()
    sdk=subprocess.check_output(['xcrun','--sdk','macosx','--show-sdk-path'],text=True).strip()
    return {'MAGPIE_MOLTENVK_LIBRARY':str(library),'MAGPIE_VULKAN_INCLUDE':str(headers),'MAGPIE_GLM_INCLUDE_DIR':str(glm),'SDKROOT':sdk},None

def main():
    if sys.platform=='win32':values,binary=prepare_windows()
    elif sys.platform=='darwin':values,binary=prepare_macos()
    else:raise SystemExit('Linux dependencies use the runner package manager. This script prepares native Windows/macOS hosts only.')
    environment=os.environ.get('GITHUB_ENV')
    if not environment:raise SystemExit('GITHUB_ENV is required to publish dependency paths to subsequent CI steps.')
    with open(environment,'a',encoding='utf-8') as target:
        for key,value in values.items():
            if '\n' in value or '\r' in value:raise RuntimeError('Invalid dependency environment value.')
            target.write(key+'='+value+'\n')
    if binary:
        with open(os.environ['GITHUB_PATH'],'a',encoding='utf-8') as target:target.write(str(binary)+'\n')
    print('Prepared native dependencies without a graphics driver or desktop session.',flush=True)

if __name__=='__main__':main()
