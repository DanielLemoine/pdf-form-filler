"""Emit the published /2 wire schemas from explicit closed contract shapes.
Semantic/reference/normalization checks remain in the strict C# validator.
"""
import json,pathlib
root=pathlib.Path(__file__).resolve().parents[1]/'schemas'
def obj(props,required=None): return {'type':'object','properties':props,'required':list(props) if required is None else required,'additionalProperties':False}
def arr(items,minimum=0,maximum=None,unique=False):
 d={'type':'array','items':items,'minItems':minimum}
 if maximum is not None:d['maxItems']=maximum
 if unique:d['uniqueItems']=True
 return d
def text(pattern=None,minimum=0,maximum=None):
 d={'type':'string','minLength':minimum}
 if pattern:d['pattern']=pattern
 if maximum:d['maxLength']=maximum
 return d
def enum(*values):return {'enum':list(values)}
boolean={'type':'boolean'};positive={'type':'integer','minimum':1};nonnegative={'type':'integer','minimum':0}
name=text('^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$');sha=text('^[0-9a-f]{64}$');nonempty=text(minimum=1)
context=obj({'taxYear':text('^[0-9]{4}$'),'quarter':enum('1','2','3','4'),'liabilityReporting':enum('none','monthly','semiweekly')})
extensions={'type':'object'}
on_state={**nonempty,'not':{'const':'Off'}}
assignment=obj({'target':nonempty,'on':on_state},['target'])
common={'key':name,'kind':enum('text','split','money','date','checkbox','oneOf'),'section':text(),'label':text(),'required':boolean,'sensitive':boolean,'pattern':text(maximum=1024),'strip':text(maximum=256),'transform':enum('upper'),'maxLength':positive}
variants=[]
for kind,props,required in [
 ('text',{'targets':arr(nonempty,1,10000,True)},['targets']),
 ('split',{'targets':arr(arr(nonempty,1,10000,True),1,10000),'parts':arr(positive,1,256)},['targets','parts']),
 ('money',{'dollars':nonempty,'cents':nonempty,'allowNegative':boolean,'thousands':boolean,'zero':enum('blank','show')},['dollars','cents','allowNegative','thousands','zero']),
 ('date',{'targets':arr(nonempty,1,10000,True),'format':enum('MMddyyyy','yyyy-MM-dd','MM/dd/yyyy')},['targets','format']),
 ('checkbox',{'target':nonempty,'on':on_state},['target']),
 ('oneOf',{'options':{'type':'object','minProperties':1,'additionalProperties':arr(assignment,1,10000)}},['options'])]:
 allowed_common={k:v for k,v in common.items() if kind not in ('money','date','checkbox') or k not in ('strip','transform')}
 variants.append(obj({**allowed_common,**props,'kind':{'const':kind}},['key','kind','required','sensitive']+required))
when={'oneOf':[obj({'key':name,'equals':{'type':['string','boolean']}}),obj({'context':enum('taxYear','quarter','liabilityReporting'),'equals':text()})]}
rules=[]
for kind,props,required in [('sum',{'total':name,'terms':arr(obj({'key':name,'sign':enum(1,-1)}),1,10000)},['total','terms']),('equal',{'left':name,'right':name},['left','right']),('requiredIf',{'keys':arr(name,1,10000,True)},['keys','when']),('allOrNone',{'keys':arr(name,1,10000,True)},['keys'])]:
 rules.append(obj({'id':name,'kind':{'const':kind},'severity':enum('error','warning'),'when':when,**props},['id','kind','severity']+required))
rect={'type':'array','items':{'type':'number'},'minItems':4,'maxItems':4}
layout=obj({'target':nonempty,'templateSha256':sha,'cells':arr(rect,8,8)})
profile=obj({'schema':{'const':'pdfform.profile/2'},'id':name,'revision':name,'profileVersion':positive,'title':nonempty,'source':{'type':'object'},'template':obj({'sha256Allowlist':arr(sha,1,None,True),'title':nonempty,'pageCount':{'type':'integer','minimum':1,'maximum':100},'structureFingerprintV1':text('^sha256:[0-9a-f]{64}$')}),'capabilities':arr(enum('text.singleWidget','checkbox.singleWidget','xfa.staticHybrid'),0,None,True),'filingYears':arr(text('^[0-9]{4}$'),1,None,True),'options':obj({'zeroAmounts':enum('blank','show'),'dateCellLayouts':arr(layout)},['zeroAmounts']),'keys':arr({'oneOf':variants},1,10000),'rules':arr({'oneOf':rules},0,1000),'unmapped':{'type':'array','maxItems':0},'extensions':extensions},['schema','id','revision','profileVersion','title','template','capabilities','filingYears','options','keys','rules','unmapped'])
data=obj({'schema':{'const':'pdfform.data/2'},'profile':text('^[A-Za-z0-9][A-Za-z0-9._-]{0,127}@[A-Za-z0-9][A-Za-z0-9._-]{0,127}$'),'context':context,'values':{'type':'object','maxProperties':10000,'additionalProperties':{'type':['string','boolean'],'maxLength':256}},'extensions':extensions},['schema','profile','context','values'])
member=obj({'alias':enum('941','941sb'),'pdf':nonempty,'profile':nonempty,'data':nonempty,'output':nonempty})
job=obj({'schema':{'const':'pdfform.job/2'},'requestId':text('^[A-Za-z0-9_-]{1,64}$'),'policy':{'const':'irs-941-packet/1'},'documents':arr(member,1,2),'flatten':boolean,'packet':obj({'output':nonempty,'pages':arr(obj({'document':enum('941','941sb'),'pages':arr(positive,1,3,True)}),1)})},['schema','requestId','policy','documents','flatten'])
artifact=obj({'id':name,'path':nonempty,'bytes':nonnegative,'sha256':sha})
file_ref=obj({'path':nonempty,'bytes':nonnegative,'sha256':sha})
completion=obj({'schema':{'const':'pdfform.completion/1'},'requestId':text('^[A-Za-z0-9_-]{1,64}$'),'toolBuild':nonempty,'result':file_ref,'artifacts':arr(artifact,1)})
finding=obj({'code':name,'stage':enum('syntax','profile','data','binding','packet','render','publication','internal'),'message':nonempty,'key':name,'field':text(),'ruleId':name,'severity':enum('error','warning')},['code','stage','message','severity'])
result=obj({'schema':{'const':'pdfform.result/2'},'requestId':text('^[A-Za-z0-9_-]{1,64}$'),'operation':nonempty,'status':enum('ok','validation_failed','input_error','profile_invalid','unsupported','mismatch','internal_error'),'exitCode':enum(0,1,2,3,4),'artifactState':enum('none','staged','published'),'validationLevels':{'type':'object','additionalProperties':enum('passed','failed','not_run')},'tool':obj({k:nonempty for k in ['name','version','build','engine','runtime','sourceReference']}),'profiles':arr(obj({'id':name,'revision':name,'profileVersion':positive,'sha256':sha})),'templates':arr(obj({'id':name,'sha256':sha})),'outputs':arr(artifact),'errors':arr(finding),'warnings':arr(finding),'stats':{'type':'object','additionalProperties':nonnegative},'filingReadiness':{'const':'not_assessed'}},['schema','operation','status','exitCode','artifactState','validationLevels','tool','profiles','templates','outputs','errors','warnings','stats','filingReadiness'])
metadata_value={'type':['object','array','string','number','boolean'],'additionalProperties':{'$ref':'#/$defs/metadataValue'},'items':{'$ref':'#/$defs/metadataValue'}}
for shape in [profile,data]:
 shape['$defs']={'metadataValue':metadata_value}
 shape['properties']['extensions']={'type':'object','additionalProperties':{'$ref':'#/$defs/metadataValue'}}
profile['properties']['source']={'type':'object','additionalProperties':{'$ref':'#/$defs/metadataValue'}}
for name,shape in [('profile',profile),('data',data),('job',job),('completion',completion),('result',result)]:
 (root/f'{name}.schema.json').write_text(json.dumps({'$schema':'https://json-schema.org/draft/2020-12/schema','$id':f'urn:pdfform:{name}:{1 if name=="completion" else 2}',**shape},indent=2)+'\n')
