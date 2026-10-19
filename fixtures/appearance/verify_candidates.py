#!/usr/bin/env python3
"""Read only owned synthetic spike PDFs; write independent verification/raster evidence beside them."""
from pathlib import Path
import argparse, hashlib, json, re
import pypdf
import fitz
from pypdf.generic import ContentStream, ByteStringObject, TextStringObject

CAP = 128 * 1024 * 1024

def require(test, code):
    if not test: raise ValueError(code)

def named_widgets(reader):
    result = {}
    for pn, page in enumerate(reader.pages, 1):
        for ref in page.get('/Annots', []):
            widget = ref.get_object()
            if widget.get('/Subtype') != '/Widget': continue
            node = widget
            parts = []
            while node:
                if '/T' in node: parts.insert(0, str(node['/T']))
                node = node.get('/Parent')
                node = node.get_object() if node else None
            name = '.'.join(parts)
            require(name not in result, 'E_DUPLICATE_PAGE_WIDGET')
            result[name] = (pn, widget)
    return result

def effective(widget, key):
    node = widget
    while node:
        if key in node: return node[key]
        node = node.get('/Parent')
        node = node.get_object() if node else None
    return None

def appearance_text(normal, reader):
    pieces=[]
    fonts=normal.get('/Resources',{}).get('/Font',{})
    current=None
    def decode(value):
        require(isinstance(value,(ByteStringObject,TextStringObject)), 'E_AP_TEXT_OPERAND')
        raw=bytes(value) if isinstance(value,ByteStringObject) else value.original_bytes
        require(current in fonts and fonts[current].get('/Encoding')=='/WinAnsiEncoding', 'E_AP_ENCODING_UNEXPECTED')
        return raw.decode('cp1252')
    for operands,op in ContentStream(normal,reader).operations:
        if op==b'Tf':current=operands[0]
        elif op==b'Tj':pieces.append(decode(operands[0]))
        elif op==b'TJ':
            pieces.extend(decode(v) for v in operands[0] if isinstance(v,(ByteStringObject,TextStringObject)))
    return ''.join(pieces)

def check_editable(path, assignments, fixture):
    reader = pypdf.PdfReader(path)
    form = reader.trailer['/Root']['/AcroForm']
    require('/XFA' not in form, 'E_XFA_REMAINS')
    require('/JavaScript' not in reader.trailer['/Root'].get('/Names', {}), 'E_CATALOG_SCRIPT_REMAINS')
    need = form.get('/NeedAppearances')
    require(need is None or need.value is False, 'E_NEED_APPEARANCES')
    fields = reader.get_fields()
    widgets = named_widgets(reader)
    require(len(widgets) == len(fixture['targets']), 'E_WIDGET_COUNT')
    counts = {'text': 0, 'checkboxes': 0, 'embeddedFontAppearances': 0, 'unembeddedFontAppearances': 0}
    for target in fixture['targets']:
        name = target['name']
        require(name in fields and name in widgets, 'E_FIELD_MISSING')
        field = fields[name]
        widget = widgets[name][1]
        normal = widget.get('/AP', {}).get('/N')
        normal = normal.get_object() if normal else None
        if target['type'] == 'Btn':
            wanted = '/' + target['onState'] if assignments['checkboxes'][name] else '/Off'
            require(str(field.get('/V')) == wanted and str(widget.get('/AS')) == wanted, 'E_CHECKBOX_STATE')
            require(str(effective(widget, '/DV')) == '/Off', 'E_CHECKBOX_DEFAULT')
            require(normal is not None and wanted in normal, 'E_CHECKBOX_AP')
            require(hashlib.sha256(normal['/'+target['onState']].get_data()).hexdigest() == target['onAppearanceSha256'], 'E_ON_AP_CHANGED')
            require(re.fullmatch(rb'\s*q\s+Q\s*', normal['/Off'].get_data()) is not None, 'E_OFF_PAINTS')
            counts['checkboxes'] += 1
        else:
            wanted = assignments['text'][name]
            require(str(field.get('/V', '')) == wanted, 'E_FIELD_VALUE')
            require(str(effective(widget, '/V') or '') == wanted, 'E_WIDGET_VALUE')
            require(str(field.get('/DV', '')) == '', 'E_DEFAULT_RETAINS_DATA')
            require('/RV' not in field and '/RV' not in widget, 'E_RICH_TEXT_REMAINS')
            require(normal is not None and hasattr(normal, 'get_data'), 'E_TEXT_AP')
            content = normal.get_data()
            actual_appearance_text=appearance_text(normal,reader)
            require(actual_appearance_text==wanted if wanted else actual_appearance_text.strip()=='','E_AP_TEXT_MISMATCH')
            if wanted:
                require(re.search(rb'/\S+\s+10(?:\.0+)?\s+Tf\b', content) is not None, 'E_FONT_SIZE_CHANGED')
                fs = normal.get('/Resources', {}).get('/Font', {})
                matching = [f.get_object() for f in fs.values() if str(f.get_object().get('/BaseFont')) == '/'+fixture['expectedFont']]
                require(len(matching) > 0, 'E_FONT_RESOURCE_CHANGED')
                desc = matching[0].get('/FontDescriptor', {})
                embedded = any(k in desc for k in ['/FontFile', '/FontFile2', '/FontFile3'])
                counts['embeddedFontAppearances' if embedded else 'unembeddedFontAppearances'] += 1
            counts['text'] += 1
    return counts

def check_flattened(path, fixture):
    r = pypdf.PdfReader(path)
    require(len(r.pages) == fixture['pages'], 'E_PAGE_COUNT')
    require(not named_widgets(r), 'E_FLATTENED_WIDGETS')
    form = r.trailer['/Root'].get('/AcroForm', {})
    require('/XFA' not in form and not form.get('/Fields', []), 'E_FLATTENED_FORM')
    require('/JavaScript' not in r.trailer['/Root'].get('/Names', {}), 'E_FLATTENED_CATALOG_SCRIPT')
    require('iText' in str(r.metadata.producer), 'E_PRODUCER_MISSING')

def chars_in(pdf, page_number, rect):
    result=[]
    for b in pdf[page_number-1].get_text('rawdict')['blocks']:
        for line in b.get('lines', []):
            for span in line['spans']:
                for c in span['chars']:
                    b=fitz.Rect(c['bbox'])
                    if rect.contains(b.tl) and rect.contains(b.br):
                        result.append({'char':c['c'],'bbox':list(b),'font':span['font'],'size':span['size']})
    return result

def main():
    parser=argparse.ArgumentParser();parser.add_argument('directory',type=Path);parser.add_argument('--render',action='store_true');a=parser.parse_args()
    directory=a.directory.resolve();manifest=json.loads((Path(__file__).parent/'fixture-manifest.json').read_text())
    fixtures={t['id']:t for t in manifest['templates']};report=json.loads((directory/'spike-report.json').read_text())
    results=[];renders=[];date=[];raster_pairs=[]
    for artifact in report['Artifacts']:
        fixture=fixtures[artifact['Template']]
        assignments=json.loads((directory/artifact['AssignmentManifest']).read_text())
        try:
            counts=check_editable(directory/artifact['Editable'],assignments,fixture)
            check_flattened(directory/artifact['Flattened'],fixture)
            results.append({'template':fixture['id'],'scenario':artifact['Scenario'],'status':'pass',**counts})
        except ValueError as ex:
            results.append({'template':fixture['id'],'scenario':artifact['Scenario'],'status':'fail','code':str(ex)})
        with fitz.open(directory/artifact['Editable']) as editable, fitz.open(directory/artifact['Flattened']) as flattened:
            for pn in range(len(editable)):
                left=editable[pn].get_pixmap(dpi=100,alpha=False).samples
                right=flattened[pn].get_pixmap(dpi=100,alpha=False).samples
                raster_pairs.append({'template':fixture['id'],'scenario':artifact['Scenario'],'page':pn+1,
                    'identical':left==right,'differentChannels':sum(a!=b for a,b in zip(left,right))})
        for kind in ['Editable','Flattened']:
            path=directory/artifact[kind]
            with fitz.open(path) as pdf:
                if a.render:
                    for pn,page in enumerate(pdf,1):
                        output=directory/(path.stem+f'.p{pn}.png')
                        page.get_pixmap(dpi=100,alpha=False).save(output)
                        renders.append(output.name)
                    if fixture['id']=='f941':
                        for label,pn,rect in [('date',2,[199,321,298,353]),('bank-combs',1,[119,707,409,747]),('ein-combs',1,[145,61,395,92])]:
                            output=directory/(path.stem+'.'+label+'.png')
                            pdf[pn-1].get_pixmap(dpi=300,alpha=False,clip=fitz.Rect(rect)).save(output);renders.append(output.name)
                    else:
                        for label,rect in [('identity',[125,85,385,165]),('daily-month1',[48,241,435,393]),('totals',[448,245,582,710])]:
                            output=directory/(path.stem+'.'+label+'.png')
                            pdf[0].get_pixmap(dpi=200,alpha=False,clip=fitz.Rect(rect)).save(output);renders.append(output.name)
                if fixture['id']=='f941' and kind=='Flattened' and artifact['Scenario']!='cleared':
                    characters=chars_in(pdf,2,fitz.Rect(205,325,291,348))
                    digits=[c for c in characters if c['char'].isdigit()]
                    gaps=[]
                    for c in digits:
                        for slash in manifest['dateGeometry']['slashRectangles']:
                            x0,y0,x1,y1=c['bbox']
                            gaps.append(max(slash[0]-x1,x0-slash[2]))
                    date.append({'scenario':artifact['Scenario'],'characters':characters,'digitCount':len(digits),
                                 'minimumHorizontalSlashGapPt':min(gaps) if gaps else None})
        require(sum(p.stat().st_size for p in directory.iterdir() if p.is_file()) <= CAP,'E_EVIDENCE_OUTPUT_CAP')
    result={'schema':'pdfform.independent-spike-verification/1','pypdf':pypdf.__version__,'pymupdf':fitz.VersionBind,
            'rasterizerFontCaveat':'PDF BaseFont identity is checked, but MuPDF can substitute an unembedded font. These renders do not establish exact TimesLTStd-Roman outlines or portable rendering.',
            'results':results,'renders':renders,'dateCharacterEvidence':date,'editableFlattenedRasterComparison':raster_pairs,
            'conclusion':'fail' if any(x['status']=='fail' for x in results) or not results else 'structural-pass-visual-review-required'}
    (directory/'independent-verification.json').write_text(json.dumps(result,indent=2)+'\n')
    print(result['conclusion'],len(results),'artifact pairs',len(renders),'renders')
    return 0 if result['conclusion']!='fail' else 4
if __name__=='__main__':raise SystemExit(main())
