#!/usr/bin/env python3
"""Capture real desktop UI states, one process at a time (popups share desktop focus)."""
import hashlib
import argparse
import html
import json
from pathlib import Path
import struct
import subprocess
import os
from wayland_popup_capture import compose

ROOT = Path(__file__).resolve().parents[1]
CASES = [
    "StageExportOutput", "NarrowStageExportOutput", "PassExportOutput", "NarrowPassExportOutput",
    "ScreenshotShortcutEdit", "NarrowScreenshotShortcutEdit", "ScreenshotShortcutOutput",
    "SourceTitleOutput",
    "SourceFrameIdentityOutput",
    "ToolbarMoveOutput",
    "ToolbarActionsOutput",
    "ProfileCapturePolicy", "NarrowProfileCapturePolicy", "NarrowProfileCaptureWarning", "NarrowProfileCapture",
    "CountdownLifecycle", "NarrowCountdownLifecycle",
    "SourceMetadataOutput",
    "ProfileImportedConfiguration", "NarrowProfileImportedConfiguration",
    "SettingsImportConfiguration", "NarrowSettingsImportConfiguration",
    "SettingsConfiguration", "NarrowSettingsConfiguration", "SettingsConfigurationFailure",
    "ToolbarPolicyOutput",
    "HomeToolbarPreferences", "NarrowHomeToolbarPreferences",
    "SettingsThemePersistence", "NarrowSettingsThemePersistence", "AboutLinks", "NarrowAboutLinks", "AboutLinkFailure",
    "LargeSourceWindowOutput",
    "HomeKeepAwake", "NarrowHomeKeepAwake", "AwakeOutput", "AwakePendingOutput", "AwakeUnavailableOutput", "AwakeFailureOutput",
    "HomePrecision", "NarrowHomePrecision", "FP16Output", "FP32Output",
    "LumaSharpenOutput", "LcasOutput",
    "ProfileCatalog", "ProfileCatalogParameters", "NarrowProfileCatalog", "NarrowProfileCatalogParameters", "CatalogXbrzOutput",
    "ProfileJincParameters", "ProfileBilateralParameters", "ModeFilterChain", "NarrowProfileJincParameters", "NarrowProfileBilateralParameters", "NarrowModeFilterChain", "JincOutput", "BilateralOutput",
    "ProfileSmaa", "ModeSmaaChain", "SmaaOutput", "NarrowProfileSmaa", "NarrowModeSmaaChain",
    "ShortcutNativeReady", "ShortcutNativeConflict", "ShortcutNativeLayoutChanged",
    "TrayOutput", "ShortcutPortal", "ShortcutPersistence", "ShortcutDuplicate", "NarrowShortcutDuplicate",
    "SettingsStartup", "SettingsStartupError", "NarrowSettingsStartup", "NarrowSettingsStartupError",
    'Home', 'HomeFramePerformance', 'HomeDuplicateMenu', 'HomeMinimumMenu', 'ProcessingOutputFrame', 'PresentationOutputFrame', 'HomeScreenshot', 'OutputFrame', 'Delayed', 'Shortcut', 'ToolbarMenu', 'NewProfile', 'NewProfileForm',
    'ScalingModes', 'ModeImport', 'ModeImportFailure', 'ModeExport', 'ModeImportedUnavailable', 'ModeImportedReplacement', 'ModeImportedParameters', 'ModeImportedEmpty', 'ProfileImportedMode', 'ModeCrt', 'ModeCrtChain', 'ModeCrtParameters', 'ModeCunny', 'ModeCunnyChain', 'ModeFsrcnnx', 'ModeFsrcnnxChain', 'ModeAnime', 'ModeAnimeChain', 'ModeFsr', 'ModeChainEditor', 'ModeRuntimeSettings', 'ModeExpand', 'ModeRename', 'ModeMore', 'ModeDelete',
    'ModeScale', 'ModeTypes', 'ModeParameters', 'ModeNew',
    'Defaults', 'ProfileCrt', 'ProfileCrtParameters', 'ProfileCunny', 'ProfileFsrcnnx', 'ProfileAnime', 'ProfileRavu', 'ModeRavuChain', 'RavuOutput', 'ProfileAnimeExpanded', 'ModeAnimeExpanded', 'AnimeExpandedL', 'AnimeExpandedVL', 'AnimeExpandedDenoiseS', 'AnimeExpandedDenoiseVL', 'ProfileSsim', 'ModeSsimChain', 'SsimOutput', 'ProfileNnedi3', 'ModeNnedi3Chain', 'Nnedi3Output', 'ProfileLifecycle', 'ProfileCropping', 'ProfileCroppingLifecycle', 'CroppingOutput', 'ProfileWindowSizing', 'ProfileWindowScaleMenu', 'ProfileOutputAlignment', 'ProfilePresentationLifecycle', 'WindowPlacementOutput', 'ProfileScreenshot', 'ProfileFsr', 'ProfileHardware', 'ProfileGraphicsDevices', 'ProfileGraphicsDeviceMenu', 'ProfileMissingGraphicsDevice', 'ProfileGraphicsDeviceRecovery', 'ProfileGraphicsQueryFailure', 'ProfileGraphicsDeviceLifecycle', 'ProfileCapture', 'ProfileCaptureWarning', 'ProfileMode', 'ProfileLimiter', 'ProfileTip',
    'ProfileMore', 'ProfileRename', 'ProfileReorder', 'ProfileDelete', 'ProfileContext',
    'Settings', 'SettingsLanguagePersistence', 'SettingsLanguage', 'SettingsTheme', 'SettingsRestart', 'About',
    'AboutChecking', 'AboutAvailable', 'AboutDownloading', 'AboutInstalling',
    'AboutDownloadFailed', 'AboutUpdated', 'AboutError', 'HomeUpdate',
    'TouchInfo', 'ExclusiveWarning', 'Toast',
    'NarrowModeImport', 'NarrowModeImportedUnavailable', 'NarrowHome', 'NarrowHomeDuplicateMenu', 'NarrowHomeMinimumMenu', 'NarrowModeCrtChain', 'NarrowModeCunnyChain', 'NarrowModeFsrcnnxChain', 'NarrowModeAnimeChain', 'NarrowModeNnedi3Chain', 'NarrowProfileNnedi3', 'NarrowProfileSsim', 'NarrowProfileAnimeExpanded', 'NarrowProfileRavu', 'NarrowModeRavuChain', 'NarrowModeAnimeExpanded', 'NarrowModeSsimChain', 'NarrowDefaults', 'NarrowProfileCropping', 'NarrowProfileWindowSizing', 'NarrowProfileWindowScaleMenu', 'NarrowProfileOutputAlignment', 'NarrowProfileGraphicsDeviceMenu', 'NarrowProfileScreenshot', 'NarrowProfileLimiter', 'NarrowSettings', 'NarrowPane', 'NarrowShortcut',
]
POPUPS = {'ProfileCatalog','ProfileCatalogParameters','NarrowProfileCatalog','NarrowProfileCatalogParameters','ModeFilterChain','NarrowModeFilterChain','ProfileJincParameters','ProfileBilateralParameters','NarrowProfileJincParameters','NarrowProfileBilateralParameters','ModeSsimChain', 'NarrowModeSsimChain','ProfileWindowScaleMenu', 'ProfileOutputAlignment', 'ModeImportedParameters', 'ProfileImportedMode', 'HomeDuplicateMenu', 'HomeMinimumMenu', 'ProfileGraphicsDeviceMenu', 'ModeCrtParameters', 'ProfileCrtParameters', 'ProfileCapture', 'Toast', 'ToolbarMenu', 'NewProfile', 'NewProfileForm', 'ModeRename', 'ModeMore',
          'ModeDelete', 'ModeScale', 'ModeTypes', 'ModeParameters', 'ModeNew',
          'ProfileMode', 'ProfileTip', 'SettingsLanguage', 'SettingsTheme',
          'ProfileMore', 'ProfileRename', 'ProfileReorder', 'ProfileDelete', 'ProfileContext'}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', default='artifacts/desktop/linux-x64/Magpie.Desktop')
    parser.add_argument('--output', default='artifacts/desktop/review')
    parser.add_argument('--case', action='append', choices=CASES)
    parser.add_argument('--language',default='en-US')
    parser.add_argument('--dark', action='store_true')
    parser.add_argument('--timeout', type=int, default=30)
    args = parser.parse_args()
    app = (ROOT / args.app).resolve()
    output = (ROOT / args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    command = ['dotnet', str(app)] if app.suffix == '.dll' else [str(app)]
    command += ['--preview-language',args.language]
    if args.dark:
        command += ['--dark']
    results = []
    for case in args.case or [case for case in CASES if case!="ShortcutPortal"]:
        target = output / f'{case}.png'
        env=dict(os.environ)
        if env.get('WAYLAND_DISPLAY'):env['WAYLAND_DEBUG']='client'
        try:
            result = subprocess.run(command + ['--preview', case, '--capture', str(target)],
                                    cwd=ROOT, env=env, text=True, capture_output=True, timeout=args.timeout)
        except subprocess.TimeoutExpired as failure:
            def decode(value): return value.decode(errors='replace') if isinstance(value,bytes) else value or ''
            (output/f'{case}-timeout.log').write_text(decode(failure.stdout)+'\n'+decode(failure.stderr))
            raise SystemExit(f'{case}: exceeded {args.timeout} seconds; output saved to {case}-timeout.log')
        (output/f'{case}.stderr.log').write_text(result.stderr,encoding='utf-8')
        if result.returncode or 'SMOKE PASS:' not in result.stdout:
            raise SystemExit(f'{case} failed ({result.returncode}):\n{result.stdout}\n{result.stderr}')
        if case in POPUPS and 'POPUP:' not in result.stdout:
            raise SystemExit(f'{case}: no visible native popup was captured.')
        if case == 'ModeTypes' and result.stdout.count('POPUP:') != 2:
            raise SystemExit('ModeTypes: nested dropdown did not render both popup layers.')
        proof=None
        if env.get('WAYLAND_DISPLAY'):
            try:proof=compose(target,result.stderr)
            except (ValueError,KeyError,FileNotFoundError) as failure:raise SystemExit(f'{case}: native popup proof failed: {failure}')
        with target.open('rb') as image:
            header = image.read(24)
        if header[:8] != b'\x89PNG\r\n\x1a\n' or struct.unpack('>II', header[16:24]) != ((750, 750) if case.startswith('Narrow') else (1418, 1050)):
            raise SystemExit(f'{case}: unexpected capture dimensions.')
        results.append({'case': case, 'image': target.name, 'app_sha256': hashlib.sha256(app.read_bytes()).hexdigest(), 'log': result.stdout.strip(),'native_popup_proof':None if proof is None else {'passed':proof['passed'],'popups':len(proof['popups']),'report':target.name+'.wayland.json'}})
        (output / 'results.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
        print(f'PASS {case}', flush=True)
    (output / 'results.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
    gallery = '<!doctype html><meta charset="utf-8"><title>Magpie UI review</title><style>body{font:16px system-ui;background:#eee;margin:24px}img{width:min(100%,1064px);display:block;border:1px solid #bbb;margin:12px 0 36px}a{color:#0067c0}</style>'
    for item in results:
        gallery += f'<h2>{html.escape(item["case"])}</h2><a href="{item["image"]}"><img loading="lazy" src="{item["image"]}"></a>'
    (output / 'index.html').write_text(gallery, encoding='utf-8')
    print(f'{len(results)} UI states captured in {output}', flush=True)

if __name__ == '__main__':
    main()
