#!/usr/bin/env python3
"""Run actual native GPU tests with locally extracted Khronos validation layers."""
from pathlib import Path
import os
import subprocess
import json

ROOT=Path(__file__).resolve().parents[1]
root=ROOT/'artifacts/test-tools/validation/root'
try:
    original=next(root.rglob('VkLayer_khronos_validation.json'))
    library=next(root.rglob('libVkLayer_khronos_validation.so'))
except StopIteration:
    raise SystemExit('Run python3 scripts/bootstrap-test-tools.py --validation first.')
manifest=json.loads(original.read_text());manifest['layer']['library_path']=str(library)
output=ROOT/'artifacts/desktop/vulkan-validation';output.mkdir(parents=True,exist_ok=True)
(output/original.name).write_text(json.dumps(manifest))
env=dict(os.environ,VK_LAYER_PATH=str(output),VK_INSTANCE_LAYERS='VK_LAYER_KHRONOS_validation')
for key in ('DISPLAY','WAYLAND_DISPLAY','WAYLAND_SOCKET'):env.pop(key,None)
result=subprocess.run([str(ROOT/'artifacts/native/linux-x64/Release/native_tests')],env=env,text=True,capture_output=True)
log=result.stdout+result.stderr
(output/'results.log').write_text(log)
print(log)
if result.returncode or 'Validation Error' in log or 'VUID-' in log:raise SystemExit('Vulkan validation failed.')
print('PASS: native GPU regression with Khronos Vulkan validation layer.')
