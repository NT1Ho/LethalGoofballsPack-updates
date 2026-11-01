"""Map Wesley-specific cassette components and JLL ItemConsumer inputs to QuickSell prefab keys."""
from pathlib import Path
import collections, json, re
here=Path(__file__).resolve().parent
packs=json.loads((here/'packages.lock.json').read_text())
need={f['sha256'] for p in packs.values() if p['package'].startswith('Magic_Wesley') for f in p['files'] if f['path'].endswith('.lethalbundle')}
rows=[]
for digest in sorted(need):
    data=json.loads((here/'quest-cache'/(digest+'.json')).read_text('utf8'))
    rows.extend((data['bundle'],r) for r in data['rows'])
items={tuple(r['id']):r for _,r in rows if r['kind']=='item'}
itemsByPrefab=collections.defaultdict(list);itemsByName=collections.defaultdict(list)
for i in items.values():
    if i['prefabName'] and i['scrap']:
        itemsByPrefab[i['prefabName']].append(i)
        itemsByName[i['item'].casefold()].append(i)
proof=collections.defaultdict(set);unmapped=set();circuits=set()
def protect(obj,source):
    if obj and obj['scrap'] and obj['prefabName']:proof[obj['prefabName']].add((obj['item'],source))
for b,r in rows:
    if r['kind']=='cassette' and r['name']:
        for i in itemsByPrefab[r['name']]:protect(i,b+':LevelCassetteItem')
        if r['unlocks'] and r['scene']:circuits.add((r['name'],r['loader'],r['scene'],b))
    if r['kind']!='consume':continue
    for key in r['accept']:
        i=items.get(tuple(key))
        if i:protect(i,b+':ItemConsumer.validItems:'+r['name'])
        else:unmapped.add((b,r['name'],str(key)))
    for rule in r['filters']:
        nm=rule['itemName']
        if not nm['shouldCheck'] or not nm['value'] or nm['compareMethod']!=0:continue
        # Vanilla Apparatus is an intentionally saleable baseline item. An acceptance
        # filter for that generic name is insufficient to protect all vanilla Apparatus.
        if nm['value'].casefold()=='apparatus':continue
        hits=itemsByName[nm['value'].casefold()]
        if not hits:unmapped.add((b,r['name'],nm['value']))
        for i in hits:protect(i,b+':ItemConsumer.validItemFilters:'+r['name'])
src=Path(r'D:/STEAM/steamapps/common/Lethal Company/BepInEx/config/gafoneo.quicksell.cfg')
config=src.read_bytes()
key=re.search(rb'(?m)^Item Blacklist = ([^\r\n]*)',config);assert key
qa=here.parent/'qa/quicksell-evidence.tsv'
qa_rows=[s.split('\t',1) for s in qa.read_text('utf8').splitlines()[1:]]
# Wrench pack uses QuickSell both for economics and mission items. Protect
# demonstrated puzzle interfaces, not every note, crafting supply or scrap.
extra={'Ticket4','HPPrefab','SAPrefab','SulfurBottlePrefab','FalseArticle','Finger','Gore','humanHeart','BlankFloppy','GuttingKnife','MatchStick','Fuse','SilverSheet'}
assert extra<=set(proof)
names=sorted({n for n,_ in qa_rows}|extra,key=str.casefold)
assert all(',' not in n and n.isascii() for n in names)
result=config[:key.end()]+b','+','.join(names).encode()+config[key.end():]
out=here.parent/'candidate/BepInEx/config'/src.name;out.parent.mkdir(exist_ok=True);out.write_bytes(result)
report={'input_bundles':len(need),'additional_prefabs':{n:sorted(proof[n]) for n in sorted(extra)},'confirmed_union':names,'unmapped_or_not_scrap':sorted(unmapped),'unlock_cassette_records':sorted(circuits)}
(here/'quest-protection.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print('protected',len(names),'unmapped or not scrap',len(unmapped),'unlock paths',len(circuits),len(need))
