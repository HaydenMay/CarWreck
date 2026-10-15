"""Extract publishable test reports from Unity logs; collection success is NOT a QA pass."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil


def blocks(text,pattern):
    result=[]; lines=text.splitlines()
    for i,line in enumerate(lines):
        if not re.search(pattern,line):continue
        part=[line]
        for row in lines[i+1:]:
            if row.startswith(('UnityEngine.','DamageLab.EditorTools.','(Filename:','\t','[')):break
            part.append(row.rstrip())
        result.append('\n'.join(part).rstrip())
    return result


def main():
    parser=argparse.ArgumentParser(); parser.add_argument('root',type=Path); args=parser.parse_args()
    root=args.root.resolve(); out=root/'Previews/CompactPickup/StageC'; folder=out/'Validation'
    final=(folder/'unity-final-suites.log').read_text(encoding='utf-8',errors='replace')
    baseline=(folder/'unity-baseline-hydrated.log').read_text(encoding='utf-8',errors='replace')
    headers={'import':r'^\[DamageLab\] Vehicle manifest','integration':r'^\[CollisionLab\] Vehicle integration:',
        'phase4':r'^\[CollisionLab\] Phase 4:', 'overflow':r'^\[CollisionLab\].*[Oo]verflow',
        'chaos':r'^\[CollisionLab\] Random impact validation', 'switching':r'^\[CollisionLab\] Vehicle switching'}
    data={name:blocks(final,pattern) for name,pattern in headers.items()}
    assert all(data.values()),'A requested suite report is missing'
    for name,values in data.items():(folder/f'{name}.txt').write_text('\n\n'.join(values)+'\n',encoding='utf-8')
    baseline_reports=blocks(baseline,headers['integration'])+blocks(baseline,headers['switching'])
    (folder/'existing_vehicles_baseline.txt').write_text('\n\n'.join(baseline_reports)+'\n',encoding='utf-8')
    pickup=next(value for value in data['integration'] if 'Vehicle integration: Compact Pickup ' in value)
    existing=[v for v in data['integration'] if 'Vehicle integration: Compact Pickup ' not in v]
    (folder/'existing_vehicles_after.txt').write_text('\n\n'.join(existing)+'\n',encoding='utf-8')
    phase=data['phase4'][0]; chaos=data['chaos'][0]; overflow='\n'.join(data['overflow'])
    coverage=[line.strip() for line in phase.splitlines() if '  [' in line and 'Box -> ' in line]
    failed=[line for line in coverage if 'FAIL' in line]
    qa=json.loads((out/'asset_qa.json').read_text()); supplemental=json.loads((out/'Unity/supplemental_validation.json').read_text())
    browser=json.loads((folder/'webgl_validation.json').read_text())
    vertices=int(re.search(r'([\d,]+) deforming vertices',pickup).group(1).replace(',',''))
    summary={'status':'NOT ACCEPTED - mandatory adaptive-contact coverage/fold criteria and visual lamp separation unresolved',
        'source_blend_sha256':hashlib.sha256((root/'Blender/CompactPickup.blend').read_bytes()).hexdigest(),
        'fbx_sha256':hashlib.sha256((root/'Assets/Vehicles/CompactPickup/Models/CompactPickup.fbx').read_bytes()).hexdigest(),
        'blender_mandatory_failures':qa['mandatory_failures'],'blender_recommendations':qa['recommended_warnings'],
        'blender_deformable_vertices':qa['info']['deformable_vertices'],'unity_deformable_vertices':vertices,
        'import_pass':all('RESULT: PASS' in v for v in data['import']),
        'integration_functional_checks_pass':'FAIL' not in pickup,
        'mandatory_fold_criteria_pass':'PART FOLD OUTLIERS' in pickup and ': none PASS' in next(line for line in pickup.splitlines() if 'PART FOLD OUTLIERS' in line),
        'coverage':{'total_targets':len(coverage),'passed':len(coverage)-len(failed),'failed':failed},
        'overflow_pass':'Every hit 1-33 applied' in overflow and 'FAIL' not in overflow,
        'chaos_pass':'Random impact validation PASS' in chaos,'switching_pass':all('Vehicle switching carries no damage, colliders or reports between vehicles PASS' in v for v in data['switching']),
        'pickup_switch_fresh':supplemental['switchFresh'],'two_colors_independent':supplemental['independentPrimary'] and supplemental['independentSecondary'],
        'webgl_browser_functional_pass':all(browser[k] for k in ('pickup_loaded','two_tone_paint_visible','front_impact_visible','random_launch_visible','repair_restored_appearance')) and browser['chaos']['hit']==50 and not browser['application_console_errors'],
        'paint_uvs_unchanged':all(s['uvUnchanged'] for s in supplemental['states']),
        'open_bed_collision_above_liner_m':supplemental['collisionAboveLiner'],
        'remaining_fold_review':next(line.strip() for line in pickup.splitlines() if 'PART FOLD OUTLIERS' in line),
        'hardware':{'cpu':'AMD Ryzen 7 5700, 8 cores / 16 logical processors','gpu':'NVIDIA GeForce RTX 4060','environment':'Windows Unity 6000.6.0f1 Editor, real PhysX scripted 50 Hz, anchored vehicle'},
        'timing_lines':[line.strip() for text in (pickup,phase,overflow,chaos) for line in text.splitlines()
            if any(key in line for key in ('deformation per hit','REBUILD OPTIMISATION','COSTS (editor','Deformation cost per hit','PERFORMANCE (editor)'))],
        'note':'No shared engine, role resistance, rigid classification or validation threshold changes. PhysX re-runs are informational; recorded replays have their own results.'}
    assert len(coverage)==16,'Coverage report did not contain all sixteen targets'
    (folder/'validation_summary.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
    web=blocks(final,r'^\[DamageLab\] WebGL build ')
    (folder/'webgl_build.txt').write_text('\n'.join(web)+'\n',encoding='utf-8')
    performance=['# Measured editor performance','',
        'Ryzen 7 5700 (8 cores / 16 threads), RTX 4060, Unity 6000.6.0f1, Windows. Real PhysX scripted at 50 Hz, anchored car.',
        'Final suites ran after Blender rendering finished. These are editor per-impact costs, not frame-rate or player-build guarantees. Memory and player frame-time budgets were not measured.',
        '',*['- '+line for line in summary['timing_lines']],
        '', 'The synchronous deformation costs can cause visible impact-time hitches. No claim of meeting a performance budget is made.']
    (folder/'performance.md').write_text('\n'.join(performance)+'\n',encoding='utf-8')
    shutil.copyfile(root/'Assets/Vehicles/CompactPickup/palette.json',out/'palette.json')
    print(json.dumps({k:summary[k] for k in ('status','blender_deformable_vertices','unity_deformable_vertices','import_pass','coverage','overflow_pass','chaos_pass','switching_pass')},indent=2))


if __name__=='__main__':main()
