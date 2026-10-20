#!/usr/bin/env python3
"""Independent raster witness: each assigned Schedule B field must add visible ink versus cleared output."""
from pathlib import Path
import json, math, sys
import fitz

root=Path(sys.argv[1]).resolve()
manifest=json.loads((Path(__file__).parent/'fixture-manifest.json').read_text())
fixture=next(x for x in manifest['templates'] if x['id']=='f941sb')
scale=3
with fitz.open(root/'f941sb-native-fixed-all-on.flattened.pdf') as populated, fitz.open(root/'f941sb-cleared.flattened.pdf') as cleared:
    p=populated[0].get_pixmap(matrix=fitz.Matrix(scale,scale),alpha=False)
    c=cleared[0].get_pixmap(matrix=fitz.Matrix(scale,scale),alpha=False)
    assert p.width==c.width and p.height==c.height and p.n==c.n==3
    ps=p.samples;cs=c.samples;records=[]
    for target in fixture['targets']:
        if target['type']!='Tx':continue
        assert target['syntheticValue']!=''
        left,bottom,right,top=target['rect'];height=populated[0].rect.height
        x0=max(0,math.ceil(left*scale)+1);x1=min(p.width,math.floor(right*scale)-1)
        y0=max(0,math.ceil((height-top)*scale)+1);y1=min(p.height,math.floor((height-bottom)*scale)-1)
        added=0
        for y in range(y0,y1):
            for x in range(x0,x1):
                index=(y*p.width+x)*3
                if cs[index]-ps[index]>=32:added+=1
        records.append({'field':target['name'],'addedDarkPixels':added,'visible':added>0})
result={'schema':'pdfform.raster-glyph-visibility/1','scale':scale,'dpi':72*scale,'template':'f941sb',
        'method':'Compare populated versus cleared owned flattened pixels inside each field rectangle; count newly darkened pixels at threshold32. Exact AP-text equality is checked separately.',
        'fieldsChecked':len(records),'allVisible':all(x['visible'] for x in records),'minimumAddedDarkPixels':min(x['addedDarkPixels'] for x in records),'fields':records,
        'limitation':'This witnesses visible added ink in the expected field, not exact portable font outlines or viewer acceptance.'}
(root/'schedule-b-raster-visibility.json').write_text(json.dumps(result,indent=2)+'\n')
print(result['fieldsChecked'],'fields;',result['allVisible'],'all visible; minimum',result['minimumAddedDarkPixels'],'added dark pixels')
raise SystemExit(0 if result['allVisible'] else 4)
