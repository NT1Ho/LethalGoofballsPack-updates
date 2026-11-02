"""Build the independent runtime data from the user's local handoff; never download content."""
import hashlib, io, json, pathlib, re, struct, sys, zipfile


def build(source, target):
    clips=json.loads(pathlib.Path(__file__).with_name('animation-clips.json').read_text('utf-8-sig'))
    with zipfile.ZipFile(source) as src:
        root = "Wesley_Images_ZH/"
        def read(path):
            return src.read(root + path)
        sums = dict(line.split("  ", 1)[::-1] for line in read("SHA256SUMS.txt").decode("utf-8-sig").splitlines())
        def checked(path):
            data = read(path)
            assert hashlib.sha256(data).hexdigest() == sums[path], path
            return data
        texts = {r["id"]: r for r in json.loads(checked("交接资料/完整译文与保留说明.json"))}
        assets = json.loads(checked("交接资料/素材与回装映射.json"))["assets"]
        entries, omitted = [], []
        assert len(texts) == len(assets) == 278 and len({r['id'] for r in assets}) == 278
        with zipfile.ZipFile(target, "w", zipfile.ZIP_STORED) as dst:
            for r in assets:
                id = r["id"]
                assert re.fullmatch(r"W\d{4}", id)
                t = texts[id]
                rules = set()
                for b in r['bindings']:
                    if b['kind'] != 'material': continue
                    for ref in b['scene_or_prefab_renderers']:
                        scene = ref[1].split(':')[0]
                        scene = scene[12:] if scene.startswith("BuildPlayer-") else ""
                        rules.add(('mesh', b['name'], b['slot'], ref[-1], scene))
                    for ref in r['script_references']:
                        if ref[-1]==b['material'] and ref[5]=='m_Material':
                            scene=ref[1]
                            rules.add(('decal',b['name'],b['slot'],ref[3],scene[12:] if scene.startswith('BuildPlayer-') else ''))
                for ref in r['script_references']:
                    if ref[4] in ('MaskStatueManager','AutopsyObjective'):
                        rules.add(('field',ref[4],ref[5],ref[3],''))
                for b in r['bindings']:
                    for clip in clips.get(id, []):
                        if b['kind']=='material':
                            rules.add(('anim',b['name'],b['slot'],clip,''))
                if not rules:
                    omitted.append(id+'\t'+r['name']+'\t只有材质依赖，缺少可核实的场景实例或控制器引用。')
                    continue
                en=checked(t['original_file'])
                native=t['native_file'] if id in {'W0020','W0021','W0022','W0023','W0024'} else None
                zh=checked(native or t['reading_file'])
                for data in (zh,en):
                    assert data.startswith(b'\x89PNG\r\n\x1a\n') and max(struct.unpack('>II',data[16:24]))<=16384
                assert hashlib.sha256(en).hexdigest()==r['png_sha256']
                assert struct.unpack('>II',en[16:24])==(r['width'],r['height'])
                if native:
                    assert struct.unpack('>II',zh[16:24])==(r['width'],r['height'])
                assert t['zh'].strip()
                entry=dict(native=bool(native),format=r['sources'][0]['texture_format'],id=id,texture=r['name'],width=r['width'],height=r['height'],title=t['title'],rules=[dict(kind=k,material=m,slot=s,path=p,scene=c) for k,m,s,p,c in sorted(rules)])
                entries.append(entry)
                if native:dst.writestr(id+'.zh.png',zh)
                else:dst.writestr(id+'.txt',t['zh'].encode('utf-8'))
            extra=json.loads(pathlib.Path(__file__).with_name('additional-readings.json').read_text('utf8'))
            for e in extra:
                assert e['id'] not in texts and e['body'].strip() and e['rules'] and not e['native']
                dst.writestr(e['id']+'.txt',e.pop('body').encode('utf8'))
                entries.append(e)
            index=json.dumps({'entries':entries},ensure_ascii=False,separators=(',',':')).encode()
            assert len(set(e['id'] for e in entries))==len(entries)==278+len(extra) and sum(e['native'] for e in entries)==5
            dst.writestr('index.json',index)
    pathlib.Path(target).with_suffix('.mapping.json').write_bytes(index)
    pathlib.Path(target).with_suffix('.omitted.tsv').write_text('ID\t资源名\t未接入原因\n'+'\n'.join(omitted)+'\n','utf-8-sig')
    print(f'{len(entries)} mapped; {len(omitted)} skipped; {len(assets)} originals checked by manifest or excluded')
if __name__=='__main__':build(*sys.argv[1:])

