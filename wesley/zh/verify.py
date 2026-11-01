"""Round-trip the literal LC Chinese cfg formats and check pinned-field coverage."""
import argparse,collections,csv,hashlib,json,pathlib,re
p=argparse.ArgumentParser();p.add_argument('candidate',type=pathlib.Path);args=p.parse_args()
z=pathlib.Path(__file__).resolve().parent;terms=args.candidate/'BepInEx'
expected=json.loads((z/'translations.zh-CN.json').read_text('utf8'))
def read_cfg(files,terminal):
 d={}
 for f in files:
  assert f.stat().st_size<=262144, f
  for row in f.read_text('utf-8-sig').splitlines():
   if not row or row.startswith('#'):continue
   if not terminal:assert row.startswith('exact:');row=row[6:]
   stop=False;tag=False;ix=-1
   for i,c in enumerate(row):
    if stop:stop=False;continue
    if c=='\\':stop=True;continue
    if terminal:
     if c=='<':tag=True
     if c=='>':tag=False
    if c=='=' and not tag:ix=i;break
   assert ix>0,(f,row[:80])
   def unescape(text):
    if terminal:return text.replace('\\n','\n').replace('\\r','\r').replace('\\t','\t').replace('\\=','=').replace('\\"','"')
    return re.sub(r'\\(.)',lambda m:{'n':'\n','r':'\r','t':'\t'}.get(m[1],m[1]),text)
   k=unescape(row[:ix]).strip();v=unescape(row[ix+1:]);assert k not in d and v.strip() and '\ufffd' not in v,k;d[k]=v
 return d
custom=read_cfg(sorted((terms/'config/V81TestChn/custom-localization/Wesley').glob('*.cfg')),False)
terminal=read_cfg(sorted((terms/'plugins/V81TestChn/translations-clean/cfg/zh-CN').glob('wesley-terminal-*.cfg')),True)
assert len(custom)<=4096 and len(custom)==len(expected)
for s,v in custom.items():
 sig=hashlib.sha256(s.encode()).hexdigest()
 assert sig in expected and expected[sig][0]==v,(sig,s[:80])
 # Input variables, rich text tags and Arabic numerals must survive. Repeated source-label
 # numbers in bilingual catalogue entries are allowed. Literal bracket prose is translatable.
 tokens=re.findall(r'\[(?:LMB|RMB|MMB|E|Q|F|R|Z|playerCredits|totalCost|currentPlanetTime)\]|\{[^{}\n]*\}|<[^<>\n]+>|\d+(?:\.\d+)*',s)
 for tok in tokens:assert tok in v,(s[:65],tok)
norm={k.replace('\r\n','\n'):v for k,v in custom.items()}
assert all(k in norm and norm[k]==v for k,v in terminal.items())
raw=z/'private'/'coverage.tsv'
e=list(csv.DictReader(raw.open(encoding='utf-8-sig'),delimiter='\t'))
fieldReport={}
for k in sorted({a for row in e for a in row['uiFields'].split('|')}):
 rows=[r for r in e if k in r['uiFields'].split('|')]
 fieldReport[k]={'candidate':sum(r['source'] in norm for r in rows),'identified':len(rows),'baseOwned':sum(r['quality']=='base-owned'for r in rows)}
logrows=[r for r in e if r['uiFields']=='text'];assert all(r['source'] in terminal for r in logrows)
bestiary=[r for r in e if 'InfoNodeDescription' in r['uiFields'].split('|')];assert all(r['source'] in terminal or r['source']=='o  o\n  o' for r in bestiary)
r={'sourceFields':len(e),'candidateDistinct':len(custom),'terminalSubset':len(terminal),'capacity':4096,'translatedQuality':dict(collections.Counter(quality for _,quality in expected.values())),'byField':fieldReport,'runtimeClaim':'static round-trip only; see external in-game evidence'}
(z/'coverage-summary.json').write_text(json.dumps(r,indent=2,ensure_ascii=False)+'\n','utf8');print(r['sourceFields'],r['candidateDistinct'],r['terminalSubset'])
