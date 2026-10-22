#!/usr/bin/env python3
"""Adapt original Magpie PS and CS effects to embedded Vulkan compute kernels."""
import argparse
from pathlib import Path
import re
import struct
import subprocess
from multipass_shaders import generate,compile_pass
from magpiefx import parse
from effect_catalog import registered

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dxc', required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--effect', action='append', help='Compile an original effect path with the graph backend; does not register a runtime effect')
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
header = '''struct Settings { uint2 inputSize; uint2 outputSize; float parameter; uint mode; float parameter2; float reserved; float4 extra[4]; };
[[vk::push_constant]] ConstantBuffer<Settings> settings;
[[vk::binding(2, 0)]] RWStructuredBuffer<uint> result;
uint2 GetInputSize() { return settings.inputSize; }
float2 GetInputPt() { return 1.0 / float2(settings.inputSize); }
'''
entry = '''
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID) {
    if (any(id.xy >= settings.outputSize)) return;
    float2 pos = (float2(id.xy) + 0.5) / float2(settings.outputSize);
    float4 color = Pass1(pos);
    uint4 rgba = uint4(round(saturate(color) * 255.0));
    result[id.y * settings.outputSize.x + id.x] = rgba.x | (rgba.y << 8) | (rgba.z << 16) | (rgba.w << 24);
}
'''
arrays = ['#pragma once\n#include <cstdint>\n#include <cstddef>\n']
effects_ir=[]
half_kernels=[]
effects = [row['path'] for row in registered('scalar')]
compute_header = '''
#define MF float
#define MF1 float1
#define MF2 float2
#define MF3 float3
#define MF4 float4
uint __Bfe(uint src, uint off, uint bits) { uint mask=(1u<<bits)-1; return (src>>off)&mask; }
uint __BfiM(uint src, uint ins, uint bits) { uint mask=(1u<<bits)-1; return (ins&mask)|(src&(~mask)); }
uint2 Rmp8x8(uint a) { return uint2(__Bfe(a,1u,3u),__BfiM(__Bfe(a,3u,3u),a,1u)); }
uint2 GetOutputSize() { return settings.outputSize; }
float2 GetOutputPt() { return 1.0 / float2(settings.outputSize); }
float4 LoadInput(int3 location) {
    if(any(location.xy < 0) || any(location.xy >= int2(settings.inputSize))) return 0;
    return INPUT.Load(location);
}
void StoreOutput(uint2 location, float4 color) {
    if(any(location >= settings.outputSize)) return;
    uint4 rgba=uint4(round(saturate(color)*255.0));
    result[location.y*settings.outputSize.x+location.x]=rgba.x|(rgba.y<<8)|(rgba.z<<16)|(rgba.w<<24);
}
'''
compute_entry = '''
[numthreads(64,1,1)]
void main(uint3 group : SV_GroupID, uint3 thread : SV_GroupThreadID) {
    Pass1(group.xy * 16, thread);
}
'''
if args.effect:
    helpers=compute_header.split('float4 LoadInput')[0]+compute_header[compute_header.index('void StoreOutput'):]
    arrays.append(generate(ROOT,args.dxc,args.output,helpers,definitions=enumerate(args.effect)))
    (args.output/'kernel.h').write_text(''.join(arrays),encoding='utf-8')
    raise SystemExit(0)
for index, name in enumerate(effects):
    source = (ROOT / f'src/Effects/{name}.hlsl').read_text(encoding='utf-8-sig')
    effect=parse(source,name)
    effects_ir.append(effect)
    if len(effect.passes)!=1:raise ValueError(f'{name}: single-pass backend received a multi-pass graph')
    if len(effect.textures)!=2 or effect.passes[0].inputs!=('INPUT',) or effect.passes[0].outputs!=('OUTPUT',):raise ValueError(f'{name}: unsupported single-pass resources')
    if len(effect.samplers)!=1 or effect.samplers[0].name!='sam' or effect.samplers[0].address!='CLAMP':raise ValueError(f'{name}: unsupported single-pass sampler')
    expected={'Nearest':(),'Lanczos':('ARStrength',),'Bilinear':(),'Bicubic':('paramB','paramC'),'FSR/FSR_EASU':(),'FSR/FSR_RCAS':('sharpness',)}
    if tuple(p.name for p in effect.parameters)!=expected[name]:raise ValueError(f'{name}: parameter ABI changed')
    output=effect.texture('OUTPUT')
    same_size=output.width=='INPUT_WIDTH' and output.height=='INPUT_HEIGHT'
    if (output.width or output.height) and not same_size:raise ValueError(f'{name}: single-pass output expressions are not implemented')
    # StubDefs is editor-only; the real helpers and resources come from this adapter.
    source = re.sub(r'^#include "(?:../)?StubDefs.hlsli"\s*$', '', source, flags=re.M)
    for parameter in effect.parameters:
        if parameter.kind!='float':raise ValueError(f'{name}: parameter type ABI changed')
        slot='settings.parameter2' if parameter.name=='paramC' else 'settings.parameter'
        source,count=re.subn(r'\bfloat\s+'+re.escape(parameter.name)+r'\s*;',f'#define {parameter.name} {slot}',source,count=1)
        if count!=1:raise ValueError(f'{name}: cannot bind parameter {parameter.name}')
    source = source.replace('Texture2D INPUT;', '[[vk::binding(0, 0)]] Texture2D<float4> INPUT;')
    source = source.replace('Texture2D OUTPUT;', '')
    source = source.replace('SamplerState sam;', '[[vk::binding(1, 0)]] SamplerState sam;')
    if name.endswith('EASU'):
        # Mathematically identical source coordinates, with an exact integer floor.
        # GPU reciprocal approximations can otherwise choose the wrong 12-tap neighborhood
        # when the source position is exactly integral (e.g. 7x5 -> 19x13).
        source = source.replace('float2 pp = ip * con0.xy + con0.zw;\n\tfloat2 fp = floor(pp);\n\tpp -= fp;', '''int2 numerator = int2(2 * ip + 1) * int2(settings.inputSize) - int2(settings.outputSize);
    int2 denominator = 2 * int2(settings.outputSize);
    int2 integral = numerator / denominator;
    integral -= int2(numerator.x < 0 && numerator.x % denominator.x != 0 ? 1 : 0,
                     numerator.y < 0 && numerator.y % denominator.y != 0 ? 1 : 0);
    float2 fp = float2(integral);
    float2 pp = float2(numerator - integral * denominator) / float2(denominator);''')
    is_compute = effect.passes[0].style=='CS'
    if is_compute and (effect.passes[0].block!=(16,16) or effect.passes[0].threads!=(64,1,1)):raise ValueError(f'{name}: unsupported dispatch mapping')
    if is_compute:
        source = source.replace('INPUT.Load(', 'LoadInput(')
        source = re.sub(r'OUTPUT\[(.*?)\]\s*=\s*(.*?);', r'if (all(\1 < settings.outputSize)) StoreOutput(\1, \2);', source, flags=re.S)
        # Helpers depend on the bound input texture declared by the effect.
        marker = '[[vk::binding(1, 0)]] SamplerState sam;'
        source = source.replace(marker, marker + compute_header)
    stem = name.replace('/', '_')
    hlsl, spirv = args.output / f'{stem}.vulkan.hlsl', args.output / f'{stem}.spv'
    hlsl.write_text(header + source + (compute_entry if is_compute else entry), encoding='utf-8')
    subprocess.run([args.dxc, '-spirv', '-fspv-target-env=vulkan1.1', '-T', 'cs_6_0', '-E', 'main', '-O3',
                    '-Fo', str(spirv), str(hlsl)], check=True)
    data = spirv.read_bytes()
    words = struct.unpack('<' + 'I' * (len(data) // 4), data)
    arrays.append(f'inline constexpr uint32_t kernel{index}[] = {{\n' + ',\n'.join(
        ','.join(f'0x{n:08x}' for n in words[i:i+8]) for i in range(0, len(words), 8)) + '\n};\n')
    half_name='nullptr';half_size='0'
    if 'FP16' in effect.capabilities:
        data=compile_pass(args.dxc,ROOT,args.output,name,stem,header+source+(compute_entry if is_compute else entry),fp16=True)
        words=struct.unpack('<'+'I'*(len(data)//4),data);half_name=f'kernel{index}Half';half_size=f'sizeof({half_name})'
        arrays.append(f'inline constexpr uint32_t {half_name}[]={{\n'+',\n'.join(','.join(f'0x{n:08x}' for n in words[i:i+8]) for i in range(0,len(words),8))+'\n};\n')
    half_kernels.append((half_name,half_size))
arrays.append('struct Kernel { const uint32_t* data; size_t size; bool linear; uint32_t block; bool sameSize; const uint32_t* halfData; size_t halfSize; };\ninline constexpr Kernel kernels[] = {\n')
for index, (name,effect) in enumerate(zip(effects,effects_ir)):
    arrays.append(f'{{kernel{index}, sizeof(kernel{index}), {"true" if effect.samplers[0].filter=="LINEAR" else "false"}, {effect.passes[0].block[0] if effect.passes[0].style=="CS" else 8}, {"true" if effect.texture("OUTPUT").width=="INPUT_WIDTH" and effect.texture("OUTPUT").height=="INPUT_HEIGHT" else "false"}, {half_kernels[index][0]}, {half_kernels[index][1]}}},\n')
arrays.append('};\n')
arrays.append(generate(ROOT,args.dxc,args.output,compute_header.split('float4 LoadInput')[0]+compute_header[compute_header.index('void StoreOutput'):]))
(args.output / 'kernel.h').write_text(''.join(arrays), encoding='utf-8')
