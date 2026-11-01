"""Produce UTF-8 exact-match extension dictionaries and the local source coverage ledger."""
import argparse,collections,csv,hashlib,json,pathlib,re
from corpus import *
def tsv(name):
 table={}
 for ln in (root/name).read_text('utf-8-sig').splitlines():
  k,v=ln.split('\t',1)
  if k in table and table[k]!=v: raise ValueError(k)
  table[k]=v
 return table
manual=tsv('glossary.tsv')|tsv('review.tsv')
editor=tsv('editorial.tsv')
terms=tsv('glossary.tsv')
title={'Burnt note':'烧焦的便条','Cunningham Travel log':'坎宁安的旅行日志','Desmond note':'德斯蒙德的便条','Explorer note':'探险者便条','Fatality report':'死亡报告','Findings':'调查发现','Forgotten log':'被遗忘的日志','Geoffrey log':'杰弗里的日志','Harrington Log':'哈林顿的日志','Lost note':'遗失的便条','Michael Report':'迈克尔的报告','Plan':'计划','Prisoner note':'囚犯的便条','Research log':'研究日志','Scientist Log':'科学家的日志','Transcript':'文字记录','Translated message':'译出的讯息','Undelivered Letter':'未寄出的信','Wet note':'湿透的便条'}
reviewed=manual|title
stable={'A00001','VIII','X','Y','Z','End','Moon','Text','Open','Next','Hide','Use','Exit','Enter','Grab','Item','Position','Rotation','Save to Json','Enter text...','Interact','interact','Go','go','Unresponsive','Goodbye','Locked','Confirm','Play','Press','Rock','Run','Push','Turn','Pull','Previous','Unknown','Disabled','Enabled','Blocked','blocked','Drop','Blank','None','Please','Fun','Visit','Life','Love','Friends','Danger','Leave','Press','press','Move','Search','Reach','Hand','Door','banana'}
parts={}
if (root/'private/draft.jsonl').is_file():parts=dict(json.loads(l) for l in (root/'private/draft.jsonl').read_text('utf8').splitlines())
fixed=root/'translations.zh-CN.json'
checked=json.loads(fixed.read_text('utf8')) if fixed.exists() else {}
allrows=collections.defaultdict(list)
for f,i,k,s in entries():allrows[s].append([f,i,k])
baseline=set((root/'base-owned.sha256').read_text('utf8').splitlines())
bodies=json.loads((root/'reviewed-bodies.zh-CN.json').read_text('utf8'))
def context_review(s,contextual):
 # Preserve the original line layout and all format tokens, even if MT merged two sentences.
 def marks(x):return re.findall(r'<[^<>\n]+>|\{[^{}\n]+\}|\[[\w /.-]{1,40}\]|[-+]?\d+(?:[.,]\d+)*',x)
 orig=s.splitlines(keepends=True)
 trans=[line.strip() for line in contextual.splitlines() if line.strip()]
 if sum(bool(x.strip()) for x in orig)==len(trans):
  it=iter(trans)
  aligned=[next(it) if x.strip() else '' for x in orig]
 else:aligned=['']*len(orig)
 result=[]
 for line,q in zip(orig,aligned):
  text=line.strip()
  if not text:result.append(line);continue
  if text in manual:q=manual[text]
  if not q or len(q)<len(text)*.12 or collections.Counter(marks(text))!=collections.Counter(marks(q)):
   file=root/'private/context-lines'/(key(text)+'.json')
   q=json.loads(file.read_text('utf8')) if file.exists() else ''
  if not q or collections.Counter(marks(text))!=collections.Counter(marks(q)):
   q=''.join(parts.get(key(p),p) if b else p for b,p in pieces(text))
  # Keys can be numbers, coordinates, placeholders or formatting-only lines.
  if collections.Counter(marks(text))!=collections.Counter(marks(q)):q=text
  result.append(line.replace(text,q,1))
 return ''.join(result)
def translate(s):
 if key(s) in bodies:return bodies[key(s)],'review'
 if s in editor:return editor[s],'editor'
 m=re.fullmatch(r'(Insert (?:false article|acid|gore|machine parts)) (\(\d+/\d+\) : \[E\])',s)
 if m:return {'Insert false article':'放入虚假报道','Insert acid':'加入酸液','Insert gore':'放入血肉','Insert machine parts':'放入机械零件'}[m[1]]+' '+m[2],'review'
 v=manual.get(s)
 if v:return v,'review'
 m=re.fullmatch(r'(.+?)(\s*:\s*)(\[[^\]]+\])',s)
 if m and m[1].strip() in manual:return manual[m[1].strip()]+m[2]+m[3],'review'
 m=re.fullmatch(r'Insert (.+?)(\s*:\s*\[[^\]]+\])?',s)
 if m and m[1].removesuffix(' ticket') in terms:
  return ('投入“'+terms[m[1][:-7]]+'”票券' if m[1].endswith(' ticket') else '装入'+terms[m[1]])+(m[2] or ''),'review'
 m=re.fullmatch(r'(-?\d+(?:-\d+)?\s+)(.+)',s)
 if m and m[2] in terms:return m[1]+terms[m[2]]+'（'+m[2]+'）','review'
 m=re.fullmatch(r'(\d+)(.+)',s)
 if m and m[2] in title:return s+'（'+title[m[2]]+'）','review'
 if guard.fullmatch(s) or s in stable or not re.search(r'[A-Za-z]{2}',s):return s,'leave'
 contextfile=root/'private'/'context'/(key(s)+'.json')
 if contextfile.exists():
  contextual=json.loads(contextfile.read_text('utf8')).replace('\r\n','\n').strip()
  if contextual:
   return context_review(s,contextual),'context-draft'
 v=''.join(parts.get(key(p),p) if b else p for b,p in pieces(s))
 if not re.search(r'[\u3400-\u9fff]',v):return s,'leave'
 # Keep typed terminal search terms visible; never change source nodes.
 if any(k=='name' for _,_,k in allrows[s]):v=s+'（'+v+'）'
 if re.search(r'\b(morse|password|spell(?:ing)?|letter is|capital letter|lowercase|bit dot|coordinates? are)\b',s,re.I):
  v+='\n\n'+s
 return v,'draft'
def esc(s):return s.replace('\\','\\\\').replace('\r','\\r').replace('\n','\\n').replace('\t','\\t').replace('=','\\=')
def tokens(s):return re.findall(r'<[^<>\n]+>|\{[^{}\n]+\}|\[(?:[A-Za-z0-9_]+|[^\]]*?[QRZEL]MB)\]',s)
ap=argparse.ArgumentParser();ap.add_argument('candidate',type=pathlib.Path);ap.add_argument('--author',action='store_true')
args=ap.parse_args()
for f,i,k,s in extra_entries(args.candidate/'BepInEx/plugins'):
 allrows[s].append([f,i,k])
for s,ref in list(allrows.items()):
 if any(k=='name' and f.startswith('CustomStoryLogs/')for f,_,k in ref):
  notification="Found journal entry: '"+s+"'"
  allrows[notification]=[[f,i,'journalNotification'] for f,i,k in ref if k=='name']
  editor[notification]='发现日志：“'+editor.get(s,s)+'”'

custom=args.candidate/'BepInEx/config/V81TestChn/custom-localization/Wesley'
custom.mkdir(parents=True,exist_ok=True)
header='# Wesley / 简体中文。仅精确匹配显示文本。\n'
chunks=[header];termchunks=[header];index={};table=[];details=[]
terminalKinds={'name','text','InfoNodeDescription','OverrideInfoNodeDescription','storyLogDescription','LevelDescription','OverrideRouteConfirmNodeDescription','OverrideRouteNodeDescription','displayText'}
terminalDir=args.candidate/'BepInEx/plugins/V81TestChn/translations-clean/cfg/zh-CN'
terminalDir.mkdir(parents=True,exist_ok=True)
for s in sorted(allrows):
 h=key(s)
 if args.author:v,quality=translate(s)
 elif h in checked:v,quality=checked[h]
 else:v,quality=s,'leave'
 if v!=s and s not in stable:
  if s!='[Data not found]' and not set(tokens(s)).issubset(tokens(v)):v,quality=s,'token-review'
  elif args.author and s in terms and re.fullmatch(r'[A-Za-z][A-Za-z0-9-]*',s):v=s+'（'+terms[s]+'）'
 if s in stable:v,quality=s,'scope-skip'
 if h in baseline:v,quality=s,'base-owned'
 if v!=s:
  if len(v)>10240:raise ValueError('unbounded output '+h)
  isTerminal=any(k in terminalKinds for _,_,k in allrows[s])
  if isTerminal:
   # LC Chinese's terminal path reads ExactMap, independently of its TMP custom table.
   def ter(x):return x.replace('\n','\\n').replace('\r','\\r').replace('\t','\\t').replace('=','\\=')
   tline=ter(s.replace('\r\n','\n'))+'='+ter(v)+'\n'
   if len((termchunks[-1]+tline).encode())>200000:termchunks.append(header)
   termchunks[-1]+=tline
  line='exact:'+esc(s)+'='+esc(v)+'\n'
  if len((chunks[-1]+line).encode())>200000:chunks.append(header)
  chunks[-1]+=line
  index[h]=[v,quality]
 refs=allrows[s];table.append([h,s,v,quality,'display-unverified',len(refs),'|'.join(sorted(set(k for _,_,k in refs))),'|'.join(sorted(set(f for f,_,_ in refs)))])
 details.extend([f,i,k,h] for f,i,k in refs)
if len(index)>4096 or len(chunks)>32:raise ValueError('LC Chinese capacity')
for i,c in enumerate(chunks,1):(custom/f'wesley-{i:02}.cfg').write_text(c,encoding='utf8',newline='\n')
for i,c in enumerate(termchunks,1):(terminalDir/f'wesley-terminal-{i:02}.cfg').write_text(c,encoding='utf8',newline='\n')
assert len(list(custom.glob('wesley-*.cfg')))==len(chunks),'Remove stale previous generated partitions'
if args.author:fixed.write_text(json.dumps(index,ensure_ascii=False,indent=1),encoding='utf8')
with (root/'private'/'coverage.tsv').open('w',encoding='utf-8-sig',newline='') as f:
 w=csv.writer(f,delimiter='\t');w.writerow(['sha256','source','translation','quality','visual','occurrences','uiFields','files']);w.writerows(table)
with (root/'private'/'locations.tsv').open('w',encoding='utf-8-sig',newline='') as f:
 w=csv.writer(f,delimiter='\t');w.writerow(['file','pathID/logIndex','field','textSHA256']);w.writerows(details)
with (root/'private'/'media.tsv').open('w',encoding='utf-8-sig',newline='') as f:
 w=csv.writer(f,delimiter='\t');w.writerow(['file','type','pathID','name']);w.writerows([d['source'],*m] for d in data for m in d['media'])
print(len(table),len(index),len(chunks),collections.Counter(v[1] for v in index.values()),collections.Counter(r[3] for r in table))
