#!/usr/bin/env python3
"""Check the migration fixture against the checked-out original initial presets."""
from pathlib import Path
import json
import re
ROOT=Path(__file__).resolve().parents[1]
source=(ROOT/'src/Magpie/AppSettings.cpp').read_text(encoding='utf-8')
body=source.split('void AppSettings::_SetInitialSettings()',1)[1].split('static std::wstring FindOldConfig',1)[0]
sections=re.split(r'auto& \w+ = _scalingModes\[\d+\];',body)[1:]
modes=json.loads((ROOT/'tests/fixtures/original-scaling-modes.json').read_text(encoding='utf-8'))['scalingModes']
assert len(sections)==len(modes)==7,'Original initial preset count changed.'
for section,mode in zip(sections,modes):
    names=[json.loads('"'+value+'"') for value in re.findall(r'\.name = L"((?:[^"\\]|\\.)*)";',section)]
    assert names==[mode['name'],*[effect['name'] for effect in mode['effects']]],(names,mode)
    values={name:float(value) for name,value in re.findall(r'parameters\[L"([^"]+)"\] = ([\d.]+)f;',section)}
    expected={name:value for effect in mode['effects'] for name,value in effect.get('parameters',{}).items()}
    assert values==expected,(values,expected)
    assert section.count('::Magpie::ScalingType::Fit')==sum(effect.get('scalingType',0)==1 for effect in mode['effects'])
assert modes[-1]['effects'][0]['scale']=={'x':2,'y':2} and 'nearest.scale = { 2.0f,2.0f };' in sections[-1]
print('PASS: original seven mode names, ordered effect paths, explicit parameters, Fit and integer-scale fixture match AppSettings.cpp.')
