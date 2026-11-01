#!/usr/bin/env python3
"""Validate prepared access material; report actual external-evidence gaps separately."""
import argparse
import json
from pathlib import Path
import re
import sys
from urllib.parse import urlsplit, unquote

ROOT = Path(__file__).resolve().parents[1]
DOC = ROOT / 'docs/components/access'

def is_reference(value):
    if not isinstance(value, str) or not value:
        return False
    u = urlsplit(value)
    return u.scheme == 'https' and bool(u.hostname) and not u.username and not u.password

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--require-external', action='store_true', help='Fail until real task, accepted contract and updated-CI references are recorded.')
    args = parser.parse_args()
    errors = []
    required = ['KT1.md','ACC-04.md','ACC-08.md','DOMAIN_MODEL.md','THREAT_MODEL.md',
                'CONTRACTS.md','BACKLOG.md','backlog.json','EVIDENCE.json','DEMO.md','VALIDATION.md']
    errors.extend('Missing ' + name for name in required if not (DOC/name).is_file())
    if errors:
        print('\n'.join(errors))
        return 1
    backlog = json.loads((DOC/'backlog.json').read_text(encoding='utf-8-sig'))
    evidence = json.loads((DOC/'EVIDENCE.json').read_text(encoding='utf-8-sig'))
    cases = {x['id']:x for x in backlog['useCases']}
    tasks = backlog['tasks']
    ids = [x['id'] for x in tasks]
    if len(ids) != len(set(ids)):
        errors.append('Duplicate task IDs')
    if {x['requirement'] for x in cases.values()} != {'ACC-04','ACC-08'}:
        errors.append('Both assigned requirements need explicit use cases')
    seen = set()
    covered = set()
    for task in tasks:
        if not task['acceptance'] or not task['prerequisites'] or not task['evidence']:
            errors.append('Incomplete card ' + task['id'])
        if not set(task['dependsOn']) <= seen:
            errors.append('Task not in dependency order: ' + task['id'])
        if not set(task['useCases']) <= cases.keys():
            errors.append('Unknown use case on ' + task['id'])
        covered.update(task['useCases'])
        seen.add(task['id'])
    if covered != cases.keys():
        errors.append('Every specified use case must be covered by a task')
    tests = '\n'.join(p.read_text() for p in (ROOT/'backend/Platform.Access.Tests').glob('*.cs'))
    threat_text = (DOC/'THREAT_MODEL.md').read_text()
    for line in threat_text.splitlines():
        if line.startswith('| TM'):
            fields = [x.strip() for x in line.strip('|').split('|')]
            if len(fields) != 6 or not all(fields):
                errors.append('Incomplete threat chain: ' + line)
                continue
            for name in fields[4].split(';'):
                if ' ' + name.strip() + '(' not in tests:
                    errors.append('Threat references nonexistent test: ' + name.strip())
    for case in cases.values():
        if case['test'] and ' ' + case['test'] + '(' not in tests:
            errors.append('Use case references nonexistent test: ' + case['test'])
    for path in DOC.glob('*.md'):
        text = path.read_text()
        for target in re.findall(r'!?\[[^\]]*\]\(([^)]+)\)', text):
            if '://' in target or target.startswith('#'):
                continue
            destination = unquote(target.split('#',1)[0])
            if destination and not (path.parent/destination).exists():
                errors.append(f'Broken local link in {path.name}: {target}')
        for index, source in enumerate(text.split('```mermaid\n')[1:]):
            body = source.split('```',1)[0].strip()
            if body not in [(p.read_text().strip()) for p in (DOC/'diagrams').glob('*.mmd')]:
                errors.append(f'Markdown diagram has no matching source: {path.name}/{index}')
    rows = [line for line in (DOC/'KT1.md').read_text().splitlines() if re.match(r'\| [1-8] \|',line)]
    if len(rows) != 8:
        errors.append('KT1 checklist must cover exactly the eight supplied obligations')
    if errors:
        for error in errors: print('FAIL:',error)
        return 1
    print(f'PASS: 8 obligations, {len(cases)} use cases, {len(tasks)} ordered cards, threat/test references, diagrams and local links.')
    gaps = []
    tapiz = evidence['tapiz']
    if not is_reference(tapiz.get('projectUrl')):
        gaps.append('Actual Tapiz project URL')
    missing = [key for key in ids if not is_reference(tapiz['taskUrls'].get(key))]
    if missing:
        gaps.append('Actual Tapiz card references: ' + ', '.join(missing))
    contract = evidence['contractReview']
    if contract.get('status') != 'ACCEPTED_AS_RECORDED' or not contract.get('reviewer') or not is_reference(contract.get('reviewUrl')):
        gaps.append('Actual neighboring-team acceptance of ' + contract['contractVersion'])
    update = evidence['preparedUpdate']
    if update.get('ciStatus') != 'SUCCESS_AS_RECORDED' or not update.get('ciCommit') or not is_reference(update.get('ciRunUrl')):
        gaps.append('Successful CI for the prepared update (baseline green run is recorded separately)')
    for gap in gaps: print('NEEDS EVIDENCE:',gap)
    if gaps:
        print('Prepared technical material is valid; full external KT1 evidence is still incomplete.')
        return 2 if args.require_external else 0
    print('Recorded external references are complete. Their authenticity/content and course approval still require actual review.')
    return 0

if __name__ == '__main__':
    sys.exit(main())
