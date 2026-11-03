// Synthetic cash is introduced by the same reviewed opening flow used by an administrator.
// It is never injected by a default company assumption in production code.
import {execFileSync} from 'node:child_process';

// Standard-library XLSX fixture: all input rows are generated synthetic test data.
export function syntheticWorkbook(sheets){
 const python=`import sys,json,io,zipfile,base64,xml.etree.ElementTree as E
sheets=json.load(sys.stdin);ns='http://schemas.openxmlformats.org/spreadsheetml/2006/main';rel='http://schemas.openxmlformats.org/officeDocument/2006/relationships'
def column(index):
 value=''
 while index:
  index,rest=divmod(index-1,26);value=chr(65+rest)+value
 return value
out=io.BytesIO()
with zipfile.ZipFile(out,'w') as z:
 workbook=E.Element('{'+ns+'}workbook');nodes=E.SubElement(workbook,'{'+ns+'}sheets');links=E.Element('Relationships')
 for i,sheet in enumerate(sheets):
  E.SubElement(nodes,'{'+ns+'}sheet',{'name':sheet['name'],'{'+rel+'}id':'r'+str(i)})
  E.SubElement(links,'Relationship',{'Id':'r'+str(i),'Target':'worksheets/s'+str(i)+'.xml'})
  doc=E.Element('{'+ns+'}worksheet');data=E.SubElement(doc,'{'+ns+'}sheetData')
  for rn,values in enumerate(sheet['rows'],1):
   row=E.SubElement(data,'{'+ns+'}row',{'r':str(rn)})
   for cn,value in enumerate(values,1):
    cell=E.SubElement(row,'{'+ns+'}c',{'r':column(cn)+str(rn),'t':'inlineStr'});inline=E.SubElement(cell,'{'+ns+'}is');E.SubElement(inline,'{'+ns+'}t').text=str(value)
  z.writestr('xl/worksheets/s'+str(i)+'.xml',E.tostring(doc))
 z.writestr('xl/workbook.xml',E.tostring(workbook));z.writestr('xl/_rels/workbook.xml.rels',E.tostring(links))
print(base64.b64encode(out.getvalue()).decode())`;
 return execFileSync('python3',['-c',python],{input:JSON.stringify(sheets),encoding:'utf8'}).trim();
}
export async function seedAccountingCash(req) {
  const rows=['BW','EX','TR'].flatMap(sponsorCode=>[`MAIN.${sponsorCode}.DEBT`,`BANK.${sponsorCode}`].map(accountCode=>({accountCode,sponsorCode,iqama:'',verifiedBalance:100000,sourceRef:`synthetic-opening:${accountCode}`,notes:'Synthetic verified cash for isolated financial integration tests'})));
  const preview=await req('/api/finance/opening/preview',{rows});
  await req('/api/finance/opening/commit',{rows:preview.rows,fingerprint:preview.fingerprint,confirm:true});
}
