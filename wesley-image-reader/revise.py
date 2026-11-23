"""Apply the 0.1.3 editorial and coverage update to the exact installed 0.1.2 archive."""
import hashlib
import json
from pathlib import Path
import re
import sys
from zipfile import ZipFile, ZIP_STORED

SOURCE_SHA256 = 'b889e40c4702ce5476b3789b950c290c0f48353c2f9d09894b86c9339c000989'


def normalize(text):
    lines = [line.strip() for line in text.splitlines()]
    return re.sub(r'\n{3,}', '\n\n', '\n'.join(lines)).strip()


def revise(source, target):
    update = json.loads(Path(__file__).with_name('revision-0.1.3.json').read_text('utf8'))
    if hashlib.sha256(source.read_bytes()).hexdigest() != SOURCE_SHA256:
        raise ValueError('Use the installed 0.1.2 Reader.wri with the recorded SHA-256.')
    edits = {id: (before, after) for id, before, after in update['edits']}
    changed = []
    with ZipFile(source) as old, ZipFile(target, 'x', compression=ZIP_STORED) as new:
        catalog = json.loads(old.read('index.json'))
        entries = catalog['entries']
        ids = {e['id'] for e in entries}
        assert len(entries) == len(ids) == 295 and edits.keys() <= ids
        for e in entries:
            id = e['id']
            if e['native']:
                new.writestr(id + '.zh.png', old.read(id + '.zh.png'))
                continue
            original = old.read(id + '.txt').decode('utf8')
            text = original
            if id in edits:
                before, after = edits[id]
                assert text.count(before) == 1, id
                text = text.replace(before, after)
            text = normalize(text)
            assert text
            if text != original:
                changed.append(id)
            new.writestr(id + '.txt', text.encode('utf8'))
        for entry in update['additions']:
            body = normalize(entry.pop('body'))
            id = entry['id']
            assert id not in ids and entry['rules'] and body and not entry['native']
            ids.add(id)
            entries.append(entry)
            new.writestr(id + '.txt', body.encode('utf8'))
        new.writestr('index.json', json.dumps(catalog, ensure_ascii=False, separators=(',', ':')).encode('utf8'))
    print(f'{len(entries)} entries, {len(changed)} old texts with edits: {", ".join(changed)}')


if __name__ == '__main__':
    revise(*map(Path, sys.argv[1:]))
