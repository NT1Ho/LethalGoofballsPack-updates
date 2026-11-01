import hashlib,json,pathlib,re,sys
root=pathlib.Path(__file__).resolve().parent
extract=root/'private'/'v2'
data=[json.loads(p.read_text('utf8')) for p in sorted(extract.glob('*.json'))]
# Keep markup, formatting, format variables, literal numbers and input bindings verbatim.
guard=re.compile(r'(\r\n|[\n\r\t]|<[^<>\n]{1,120}>|\[[^\[\]\n]{1,150}\]|\{[^{}\n]{1,128}\}|%[sdif]|\b(?:[IVX]{2,}|I(?= for the fallen))\b|[-+]?\d+(?:[.,]\d+)*%?)')
fields={'name','text','PlanetName','LevelDescription','headerText','subText','hoverTip','holdTip','disabledHoverTip','toolTips[]','itemName','enemyName','m_text','displayText','storyLogDescription','dungeonDisplayName'}
def entries():
 for d in data:
  for id,k,s in d['rows']:
   if s.strip() and (k in fields or k.rsplit('.',1)[-1] in {'InfoNodeDescription','OverrideRouteNodeDescription','OverrideRouteConfirmNodeDescription','EnemyDisplayName','DungeonName','OverrideInfoNodeDescription','OverrideBuyConfirmNodeDescription','OverrideBuyNodeDescription','EnabledTriggerName','DisabledTriggerName','TriggerDisplayText','TriggerString (progress)','CursedMessages[]','CursedMessagesSpawned[]','ModifierMessages[]','header','message','WeatherName'}):yield d['source'],id,k,s.strip()
def pieces(s):
 for i,v in enumerate(guard.split(s)):
  if not v:continue
  if i%2 or not re.search('[A-Za-z]',v):yield False,v;continue
  # Independent sentences bound model context without cutting words or silently truncating long notes.
  for q in re.split(r'(?<=[.!?])\s+(?=[A-Z])',v):
   pre=q[:len(q)-len(q.lstrip())];post=q[len(q.rstrip()):];t=q.strip()
   if not t:yield False,q;continue
   if pre:yield False,pre
   yield True,t
   if post:yield False,post
def key(s):return hashlib.sha256(s.encode()).hexdigest()

# These literals are emitted directly by methods in the 1.0.25 Shenanigans binary.
def extra_entries(plugins):
 import hashlib
 dll=plugins/'Magic_Wesley-Wesleys_Shenanigans'/'WesleysInteriorTools.dll'
 if not dll.exists():return
 with dll.open('rb') as f:sig=hashlib.file_digest(f,'sha256').hexdigest()
 expected='41e1224084885644fd4522c1eb544696e10eb52631d21a9a3be1b5a3b538b3f7'
 if sig!=expected:raise ValueError('Recheck display literals against the new Shenanigans DLL')
 for method,texts in {
 'ClipboardItemButCool.EquipItem':['To read the manual:','Press Z to inspect closely. Press Q and E to flip the pages.'],
 'MoveTriggersA/B':['Play ??? : [E]'],
 'CraneVerticalTrigger':['Raise crane hook : [E]'],
 'ModifierMessages':['We pour II less from each for our sins but we add I for the seer','We pour II extra to each for our glory but remove I for the fallen','We lay still','We pour III extra to each for the warrior but II less for the poor','We pour I extra to each for our love and remove I for the betrayal']
 }.items():
  for t in texts:yield str(dll.relative_to(plugins)),method,'runtimeLiteral',t
