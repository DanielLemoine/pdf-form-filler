"""Offline, deterministic migration of the two legacy profiles/synthetic samples.
Only schema/type/rule/normalization policy corrections from Rev.2; preserves every ordered target.
No PDF, network, tax calculation, or arbitrary profile admission occurs here.
"""
import copy, hashlib, json, pathlib, sys
root = pathlib.Path(__file__).resolve().parents[1]
source = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else root/'fixtures/contracts/legacy'
manifest=[]
for original in sorted((source/'profiles').glob('*.json')):
    old=json.loads(original.read_text()); p=copy.deepcopy(old)
    if p['id'] not in ('irs-941','irs-941sb'): raise ValueError('Unsupported source profile')
    p['schema']='pdfform.profile/2'; p['profileVersion']=1
    p.pop('status',None)
    p['template']={'sha256Allowlist':[p['source']['sha256']], 'title':p['match']['title'], 'pageCount':3 if p['id']=='irs-941' else 1, 'structureFingerprintV1':p['match']['structureFingerprint']}
    p.pop('match'); p['capabilities']=['text.singleWidget','checkbox.singleWidget','xfa.staticHybrid']; p['filingYears']=['2026']
    for k in p['keys']:
        k['required']=k.get('required',False); k['sensitive']=k.get('sensitive',False)
        if 'pattern' in k: k['pattern']=k['pattern'].replace(r'\d','[0-9]')
        if k['kind']=='money':
            k['allowNegative']=k.get('allowNegative',False); k['thousands']=k.get('thousands',True); k['zero']=k.get('zero',p['options']['zeroAmounts'])
        if k['kind']=='oneOf':
            k['options']={choice:[{'target':t} for t in (targets if isinstance(targets,list) else [targets])] for choice,targets in k['options'].items()}
        if k['key']=='line15c.routingNumber': k['pattern']='^(0[1-9]|1[0-2]|2[1-9]|3[0-2])[0-9]{7}$'
        if k['key']=='line15e.accountNumber':
            k['pattern']='^[A-Za-z0-9-]{1,17}$'; k['label']='15e. Account number (up to 17 ASCII letters, digits or hyphens)'
    for i,r in enumerate(p['rules'],1):
        r['id']=f'{p["id"]}.rule{i:02d}'; r['severity']=r.get('severity','error')
        if r['kind']=='sum': r['terms']=[{'key':t.lstrip('-'),'sign':-1 if t.startswith('-') else 1} for t in r['terms']]
        if r.get('total')=='line16.total' or r.get('keys')==['line16.month1','line16.month2','line16.month3','line16.total']:
            r['when']={'context':'liabilityReporting','equals':'monthly'}
        if r.get('when',{}).get('key')=='line17.businessClosed': r['when']['equals']=True
    if p['id']=='irs-941':
        p['rules'].append({'id':'irs-941.monthly-line12','kind':'equal','severity':'error','left':'line16.total','right':'line12.totalTaxes','when':{'context':'liabilityReporting','equals':'monthly'}})
        # Separately reviewed source geometry; never infer date placement from current field width.
        p['profileVersion']=2
        p['options']['dateCellLayouts']=[json.loads((root/'fixtures/contracts/date-layout-941.json').read_text())]
    def mapping(profile):
        out=[]
        for k in profile['keys']:
            def walk(x):
                if isinstance(x,str): return [x]
                if isinstance(x,list): return [v for t in x for v in walk(t)]
                return [x['target']]
            targets=[]
            for prop in ('targets','dollars','cents','target'):
                if prop in k: targets+=walk(k[prop])
            if 'options' in k:
                for target in k['options'].values(): targets+=walk(target)
            out.append({'key':k['key'],'targets':targets})
        return out
    assert mapping(old)==mapping(p),'Migration changed reviewed mapping'
    target=root/'profiles'/original.name; target.write_text(json.dumps(p,indent=2,ensure_ascii=False)+'\n')
    expected=root/'tests/PdfForm.Core.Tests'/f'{p["id"]}.expected-mapping.json'; expected.write_text(json.dumps(mapping(old),indent=2)+'\n')
    manifest.append({'id':p['id'],'legacyFixtureSha256':hashlib.sha256(original.read_bytes()).hexdigest(),'migratedProfileSha256':hashlib.sha256(target.read_bytes()).hexdigest(),'keys':len(p['keys']),'targets':sum(len(x['targets']) for x in mapping(p))})
for original in sorted((source/'samples').glob('*.json')):
    old=json.loads(original.read_text())
    if old['schema']!='pdfform.data/1':continue
    p=copy.deepcopy(old); p['schema']='pdfform.data/2'; p.pop('comment',None)
    reporting='semiweekly' if ('941sb' in original.name or 'semiweekly' in original.name) else 'monthly'
    p['context']={'taxYear':'2026','quarter':'3','liabilityReporting':reporting}
    (root/'samples'/original.name).write_text(json.dumps(p,indent=2)+'\n')
job={'schema':'pdfform.job/2','requestId':'synthetic-2026q3-001','policy':'irs-941-packet/1','documents':[{'alias':'941','pdf':'../fixtures/appearance/forms/f941.pdf','profile':'../profiles/irs-941-2026-03.json','data':'941-2026q3-semiweekly.json','output':'941.pdf'},{'alias':'941sb','pdf':'../fixtures/appearance/forms/f941sb.pdf','profile':'../profiles/irs-941sb-2024-03.json','data':'941sb-2026q3.json','output':'941sb.pdf'}],'flatten':True,'packet':{'output':'return-packet.pdf','pages':[{'document':'941','pages':[1,2]},{'document':'941sb','pages':[1]}]}}
(root/'samples/job-2026q3-semiweekly.json').write_text(json.dumps(job,indent=2)+'\n')
(root/'fixtures/contracts/profile-migration-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
