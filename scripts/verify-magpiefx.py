#!/usr/bin/env python3
"""Verify the real MagpieFX corpus and independent front-end failure/size cases."""
import argparse
from collections import Counter
from dataclasses import asdict
import hashlib
import json
from pathlib import Path
import re
from magpiefx import FORMATS, FxError, SizeExpression, parse, texture_sizes

ROOT=Path(__file__).resolve().parents[1]
BASE='''//!MAGPIE EFFECT
//!VERSION 4
//!TEXTURE
Texture2D INPUT;
//!TEXTURE
Texture2D OUTPUT;
//!PASS 1
//!STYLE PS
//!IN INPUT
//!OUT OUTPUT
float4 Pass1(float2 pos) { return INPUT.Load(int3(0,0,0)); }
'''

def verify_boundaries():
    count=0
    def reject(source,expected):
        nonlocal count
        try:parse(source,'invalid.hlsl')
        except FxError as error:
            assert expected in error.message,(expected,str(error))
            assert error.source=='invalid.hlsl' and error.line>0
            count+=1;return
        raise AssertionError('Invalid MagpieFX accepted: '+expected)
    reject(BASE.replace('VERSION 4','VERSION 3'),'VERSION 4')
    reject(BASE.replace('//!VERSION 4','//!VERSION 4\n//!VERSION 4'),'Duplicate VERSION')
    reject(BASE.replace('MAGPIE EFFECT','MAGPIE SHADER'),'MAGPIE EFFECT')
    reject(BASE+'\n/* unterminated','Unterminated')
    reject(BASE+'\0','NUL')
    reject(BASE.replace('//!PASS 1','//!PASS 2'),'contiguous')
    reject(BASE.replace('//!PASS 1','//!PASS 1\n//!IN INPUT'),'Duplicate IN')
    reject(BASE.replace('//!OUT OUTPUT','//!OUT INPUT'),'INPUT is read-only')
    reject(BASE.replace('//!IN INPUT','//!IN OUTPUT'),'OUTPUT is write-only')
    reject(BASE.replace('//!IN INPUT','//!IN missing'),'undeclared')
    reject(BASE.replace('//!IN INPUT','//!IN INPUT, INPUT'),'duplicate IN')
    reject(BASE.replace('//!STYLE PS','//!STYLE PS\n//!BLOCK_SIZE 8'),'PS style')
    reject(BASE.replace('//!STYLE PS','//!STYLE CS'),'Missing BLOCK_SIZE')
    reject(BASE.replace('//!STYLE PS','//!STYLE CS\n//!BLOCK_SIZE 0\n//!NUM_THREADS 64'),'positive uint32')
    reject(BASE.replace('//!STYLE PS','//!STYLE CS\n//!BLOCK_SIZE 8\n//!NUM_THREADS 32,64'),'HLSL limits')
    reject(BASE.replace('//!STYLE PS','//!STYLE CS\n//!BLOCK_SIZE 8,8,8\n//!NUM_THREADS 64'),'dispatch dimensions')
    reject(BASE.replace('//!STYLE PS','//!STYLE GLSL'),'STYLE must')
    reject(BASE.replace('Texture2D INPUT;','Texture2D INPUT;\n//!TEXTURE\nTexture2D INPUT;'),'Duplicate texture')
    reject(BASE.replace('//!TEXTURE\nTexture2D INPUT;','//!TEXTURE\n//!WIDTH 3\n//!HEIGHT 2\nTexture2D INPUT;'),'INPUT cannot')
    reject(BASE.replace('//!TEXTURE\nTexture2D OUTPUT;','//!TEXTURE\n//!WIDTH 3\nTexture2D OUTPUT;'),'appear together')
    reject(BASE.replace('//!TEXTURE\nTexture2D OUTPUT;','//!TEXTURE\n//!WIDTH OUTPUT_WIDTH\n//!HEIGHT INPUT_HEIGHT\nTexture2D OUTPUT;'),'refer to itself')
    texture='//!TEXTURE\n//!FORMAT R16_FLOAT\n//!WIDTH INPUT_WIDTH\n//!HEIGHT INPUT_HEIGHT\nTexture2D temp;\n'
    reject(BASE.replace('//!PASS 1',texture.replace('R16_FLOAT','INVALID')+'//!PASS 1'),'Unknown texture FORMAT')
    reject(BASE.replace('//!PASS 1',texture.replace('//!FORMAT R16_FLOAT\n','')+'//!PASS 1'),'needs FORMAT or SOURCE')
    reject(BASE.replace('//!PASS 1',texture.replace('//!FORMAT R16_FLOAT','//!SOURCE asset.dds')+'//!PASS 1'),'SOURCE cannot')
    reject(BASE.replace('//!PASS 1','//!SAMPLER\n//!FILTER CUBIC\nSamplerState s;\n//!PASS 1'),'FILTER/ADDRESS')
    reject(BASE.replace('//!PASS 1','//!SAMPLER\n//!FILTER POINT\nSamplerState INPUT;\n//!PASS 1'),'Duplicate symbol')
    parameter='//!PARAMETER\n//!DEFAULT 1\n//!MIN 0\n//!MAX 2\n//!STEP 1\nint p;\n'
    for changed,expected in [(parameter.replace('DEFAULT 1','DEFAULT 3'),'outside MIN/MAX'),(parameter.replace('DEFAULT 1','DEFAULT 1.2'),'Invalid int'),(parameter.replace('DEFAULT 1','DEFAULT 2147483648'),'Out of range'),(parameter.replace('//!STEP 1\n',''),'Missing STEP')]:
        reject(BASE.replace('//!PASS 1',changed+'//!PASS 1'),expected)
    assert parse(BASE.replace('//!PASS 1',parameter+'//!PASS 1')).parameters[0].kind=='int'
    reject(BASE.replace('//!PASS 1',parameter.replace('int p;','float p;').replace('DEFAULT 1','DEFAULT 1e100')+'//!PASS 1'),'Out of range')
    fixture='''//!MAGPIE EFFECT
//!VERSION 4
//!CAPABILITY FP16
//!USE MulAdd
/* //!PASS 99
//!VERSION 1
*/
//!TEXTURE
//!WIDTH INPUT_WIDTH * 2
//!HEIGHT INPUT_HEIGHT * 3
Texture2D OUTPUT;
//!TEXTURE
//!WIDTH OUTPUT_WIDTH / 2
//!HEIGHT INPUT_HEIGHT
//!FORMAT R16_FLOAT
Texture2D halfTexture;
//!TEXTURE
//!WIDTH INPUT_WIDTH
//!HEIGHT INPUT_HEIGHT
//!FORMAT R8G8_UNORM
Texture2D packed;
//!SAMPLER
//!FILTER LINEAR
//!ADDRESS WRAP
SamplerState wrapped;
//!COMMON
#define LABEL "http://example.test/texture"
// ordinary comment: //!PASS 100
float identity(float x) { return x; }
//!PASS 2
//!STYLE PS
//!IN halfTexture, packed
//!OUT OUTPUT
float4 Pass2(float2 pos) { return 1; }
//!PASS 1
//!IN INPUT
//!OUT halfTexture, packed
//!BLOCK_SIZE 32,16
//!NUM_THREADS 32,8
void Pass1(uint2 start, uint3 id) { }
'''
    effect=parse(fixture,'fixture.hlsl')
    assert effect.uses==('MULADD',) and effect.capabilities==('FP16',)
    assert [p.number for p in effect.passes]==[1,2] and effect.passes[0].threads==(32,8,1)
    assert effect.passes[0].outputs==('halfTexture','packed') and effect.samplers[0].address=='WRAP'
    assert 'http://example.test/texture' in effect.common and 'identity' in effect.common
    assert texture_sizes(effect,(7,5),(99,99))=={'INPUT':(7,5),'OUTPUT':(14,15),'halfTexture':(7,5),'packed':(7,5)}
    reject(fixture.replace('//!IN INPUT','//!IN INPUT, halfTexture'),'must be distinct')
    reject(fixture.replace('//!OUT halfTexture, packed','//!OUT OUTPUT'),'Only the last')
    reject(fixture.replace('//!IN halfTexture, packed','//!IN halfTexture').replace('//!OUT OUTPUT','//!OUT packed'),'Last pass')
    assert parse('\ufeff// Copyright\r\n'+BASE.replace('\n','\r\n')).passes[0].style=='PS'
    assert parse(BASE.replace('//!VERSION 4','//!VERSION 4\n//!FUTURE true')).warnings
    common_fx=parse(BASE.replace('//!PASS 1','//!COMMON\nfloat a() { return 0; }\n//!COMMON\nfloat b() { return 1; }\n//!PASS 1'))
    assert 'float a()' in common_fx.common and 'float b()' in common_fx.common
    assert common_fx.texture('INPUT').format==common_fx.texture('OUTPUT').format=='R8G8B8A8_UNORM'
    for text,expected in [('INPUT_WIDTH*2+1',15),('INPUT_HEIGHT / 2',3),('2^3^2',512),('max(3, ceil(INPUT_WIDTH/2))',4),('min(7)',7),('abs(-2)+sqrt(9)',5),('1e1',10)]:
        assert SizeExpression(text).evaluate({'INPUT_WIDTH':7,'INPUT_HEIGHT':5})==expected,text
    for text in ["__import__('os')",'INPUT_WIDTH.__class__','[1][0]','x','1 if 1 else 2','2**3','True','min(x=1)','1/0','1e1000','sqrt(-1)','0','16385','9^9^9','-2']:
        try:SizeExpression(text).evaluate({'INPUT_WIDTH':7,'INPUT_HEIGHT':5})
        except FxError:count+=1
        else:raise AssertionError('Invalid/unsupported dimension expression accepted: '+text)
    return count


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=ROOT/'artifacts/magpiefx/frontend.json')
    args=parser.parse_args()
    # A previous successful report must not survive a failed current verification.
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps({'passed':False,'state':'running'})+'\n')
    negative=verify_boundaries();catalog=[];totals=Counter();dependencies={}
    formats_source=(ROOT/'src/Magpie.Core/include/EffectDesc.h').read_text()
    original_formats=re.search(r'enum class EffectIntermediateTextureFormat \{(.*?)UNKNOWN',formats_source,re.S)[1]
    assert set(re.findall(r'\bR\w+\b',original_formats))==FORMATS.keys()
    for path in sorted((ROOT/'src/Effects').rglob('*.hlsl')):
        text=path.read_text(encoding='utf-8-sig');name=str(path.relative_to(ROOT));effect=parse(text,name)
        # Independent source census prevents silently dropping declarations or pass blocks.
        for tag,values in [('PASS',effect.passes),('PARAMETER',effect.parameters),('SAMPLER',effect.samplers)]:
            assert len(values)==len(re.findall(r'^//!'+tag+r'\b',text,re.M)),(name,tag)
        sizes=texture_sizes(effect,(13,9),(31,23))
        if path.name=='NNEDI3_nns16_win8x4.hlsl':assert sizes['OUTPUT']==(26,18) and sizes['temp']==(13,18)
        if path.name=='NIS.hlsl':assert sizes['coef_scaler'] is None and effect.texture('coef_scaler').source=='Coef_Scale.dds'
        resources=[]
        for texture in effect.textures:
            item=asdict(texture);item['probe_size']=sizes[texture.name];resources.append(item)
            if texture.source:
                asset=path.parent/texture.source
                assert asset.is_file(),asset
                dependencies[str(asset.relative_to(ROOT))]=hashlib.sha256(asset.read_bytes()).hexdigest()
            if texture.format:totals['format_'+texture.format]+=1
        totals.update(effects=1,passes=len(effect.passes),parameters=len(effect.parameters),textures=len(effect.textures),samplers=len(effect.samplers))
        totals['declares_fp16']+='FP16' in effect.capabilities
        for p in effect.passes:totals['style_'+p.style]+=1
        catalog.append({'name':name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'sort_name':effect.sort_name,'capabilities':effect.capabilities,'uses':effect.uses,'parameters':[asdict(p) for p in effect.parameters],'textures':resources,'samplers':[asdict(s) for s in effect.samplers],'passes':[{k:v for k,v in asdict(p).items() if k!='code'} for p in effect.passes],'warnings':effect.warnings})
    report={'passed':True,'scope':'Build-time metadata and bounded arithmetic sizes; not full shader compilation/runtime execution','negative_cases':negative,'supported_format_metadata':len(FORMATS),'totals':totals,'external_asset_sha256':dependencies,'effects':catalog}
    args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'PASS: MagpieFX v4 front end, {totals["effects"]} original effects / {totals["passes"]} passes / {totals["parameters"]} parameters, {negative} invalid cases; runtime backend support is a separate gate.')

if __name__=='__main__':main()
