// Shared table behavior. New grids use table(), not standalone <table> markup.
let gridSequence = 0;
const numericTableHeaders = new Set(['الطلبات','المبلغ','رصيد سابق','حركة الشهر','الرصيد الختامي','إيراد المنصة','استحقاق المندوب']);
function renderDataTable(headers:string[],rows:string[]):string {
    const id='grid-'+(++gridSequence);
    return `<section class="data-grid" id="${id}"><div class="grid-toolbar"><span data-grid-count aria-live="polite">${rows.length} من ${rows.length} سجل</span><div class="actions"><button class="secondary" data-grid-export="all">تصدير Excel — الكل</button><button class="secondary" data-grid-export="filtered">تصدير المفلتر</button><button class="secondary" data-grid-csv>تصدير CSV المفلتر</button><button class="ghost" data-grid-clear>مسح الفلاتر</button></div></div><div class="table-wrap"><table><thead><tr>${headers.map(h=>`<th scope="col" data-header="${esc(h)}" data-numeric="${numericTableHeaders.has(h)}">${esc(h)}${h?`<button class="column-filter-toggle ghost" type="button" data-column-menu="${headers.indexOf(h)}" aria-label="خيارات العمود ${esc(h)}">⌄</button>`:''}</th>`).join('')}</tr><tr class="column-filters">${headers.map((h,i)=>`<th>${h?`<input type="search" data-column-filter="${i}" aria-label="فلتر ${esc(h)}" placeholder="فلتر ${esc(h)}" autocomplete="off">`:''}</th>`).join('')}</tr></thead><tbody>${rows.join('')}</tbody></table></div><p class="filter-empty hide">لا توجد صفوف تطابق الفلاتر.</p></section>`;
}
function excelControls(kind:'choices'|'riders'):string {
    return `<a class="button secondary" href="/api/excel/${kind}/template">تنزيل قالب Excel</a><button class="secondary" data-table-import="${kind}">استيراد Excel</button>`;
}
function filterDataGrid(grid:HTMLElement):void {
    const filters=[...grid.querySelectorAll<HTMLInputElement>('[data-column-filter]')];
    const rows=[...grid.querySelectorAll<HTMLTableRowElement>('tbody>tr')];
    const norm=(text:string)=>text.normalize('NFKC').toLocaleLowerCase('ar').replace(/\s+/g,' ').trim();
    let visible=0;
    for(const row of rows){row.hidden=!filters.every(f=>!f.value||norm(row.cells[Number(f.dataset.columnFilter)]?.textContent||'').includes(norm(f.value)))||!columnSelectionMatches(grid,row);if(!row.hidden)visible++;}
    grid.querySelector<HTMLElement>('[data-grid-count]')!.textContent=`${visible} من ${rows.length} سجل`;
    grid.querySelector('.filter-empty')!.classList.toggle('hide',visible!==0||rows.length===0);
}
async function downloadExcel(headers:string[],rows:(string|number)[][],numericColumns:number[],filename:string):Promise<void> {
    const res=await fetch('/api/excel/export',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({headers,rows,numericColumns})});
    if(!res.ok){const error=await res.json().catch(()=>({}));throw Error(error.error||'تعذر تصدير Excel');}
    const url=URL.createObjectURL(await res.blob());const link=document.createElement('a');link.href=url;link.download=filename;document.body.append(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);
}
async function exportDataGrid(grid:HTMLElement,filtered:boolean):Promise<void> {
    const th=[...grid.querySelectorAll<HTMLTableCellElement>('thead>tr:first-child>th')];
    const indices=th.map((h,i)=>h.dataset.header?.trim()?i:-1).filter(i=>i>=0);
    const headers=indices.map(i=>th[i].dataset.header!);
    const numeric=indices.map((i,j)=>th[i].dataset.numeric==='true'?j:-1).filter(i=>i>=0);
    const rows=[...grid.querySelectorAll<HTMLTableRowElement>('tbody>tr')].filter(row=>!filtered||!row.hidden).map(row=>indices.map(i=>(row.cells[i]?.textContent||'').trim()));
    await downloadExcel(headers,rows,numeric,`${active}-${filtered?'filtered':'all'}.xlsx`);
}
let tableImportState:any=null;
async function tableImportDialog(kind:'choices'|'riders'):Promise<void> {
    tableImportState=null;
    const campaigns=kind==='choices'?(await api('/campaigns')).filter(c=>c.status==='open'&&Date.parse(c.expiresAt)>Date.now()):[];
    modal(kind==='choices'?'استيراد اختيارات المناديب':'استيراد مناديب جدد',`<div class="notice">استخدم القالب دون تعديل عناوين الأعمدة أو ترتيبها. الاستيراد يضيف سجلات جديدة فقط؛ الردود المستوردة تنتظر المراجعة.</div><a class="button secondary" href="/api/excel/${kind}/template">تنزيل القالب المعتمد</a>${kind==='choices'?`<label>حملة الاختيارات<select id="table-import-campaign" required><option value="">اختر الحملة</option>${options(campaigns.map(c=>[c.id,`${c.title} — ${c.id.slice(0,8)}`]))}</select></label>${!campaigns.length?'<div class="notice warn">أنشئ حملة استقبال مفتوحة أولًا من شاشة النماذج.</div>':''}`:''}<label>ملف Excel<input id="table-import-file" type="file" accept=".xlsx"></label><button id="table-import-preview">معاينة وفحص الملف</button><div id="table-import-result"></div>`);
    const invalidate=()=>{tableImportState=null;$('#table-import-result').innerHTML='';};
    $('#table-import-file').onchange=invalidate;if($('#table-import-campaign'))$('#table-import-campaign').onchange=invalidate;
    $('#table-import-preview').onclick=async()=>{
        const button=$('#table-import-preview');button.disabled=true;
        try {
            const file:File=$('#table-import-file').files[0];if(!file)throw Error('اختر ملف Excel أولًا.');if(file.size>8*1024*1024)throw Error('حد الملف8ميجابايت.');
            const campaignId=$('#table-import-campaign')?.value||'';if(kind==='choices'&&!campaignId)throw Error('اختر حملة استقبال الاختيارات.');
            const dataBase64=await new Promise<string>((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(String(reader.result).split(',')[1]);reader.onerror=()=>reject(Error('تعذرت قراءة الملف.'));reader.readAsDataURL(file);});
            const payload={filename:file.name,dataBase64,campaignId};const result=await api(`/excel/${kind}/preview`,payload);
            tableImportState={kind,payload,fingerprint:result.fingerprint,errors:result.errors};
            const headers=kind==='choices'?['صف Excel','الإقامة','الاسم','الاختيار']:['صف Excel','الإقامة','الاسم','الجوال','المدينة','معرف السياسة'];
            const rows=result.rows.slice(0,20).map(r=>`<tr><td>${r.row}</td><td>${esc(r.data.iqama)}</td><td>${esc(r.data.name)}</td>${kind==='choices'?`<td>${esc(systems[r.data.workSystem])}</td>`:`<td>${esc(r.data.phone)}</td><td>${esc(r.data.city)}</td><td>${esc(r.data.policyId)}</td>`}</tr>`);
            $('#table-import-result').innerHTML=`<h3>نتيجة الفحص</h3><div class="notice ${result.errors.length?'error':''}">${result.rows.length} صف قابل للقراءة · ${result.errors.length} خطأ. ${result.errors.length?'لن يُحفظ أي صف قبل معالجة جميع الأخطاء.':''}</div>${result.errors.length?`<div class="import-errors">${result.errors.slice(0,100).map(e=>`<div>الصف ${e.row}: ${esc(e.message)}</div>`).join('')}</div><button class="secondary" id="table-import-errors">تنزيل كل الأخطاء Excel</button>`:''}<p class="muted small">المعاينة التالية لأول20سجل فقط؛ اعتماد الاستيراد يحفظ الدفعة كاملة إذا خلت من الأخطاء.</p>${table(headers,rows)}<button id="table-import-commit" ${result.errors.length||!result.rows.length?'disabled':''}>تأكيد استيراد ${result.rows.length} سجل</button>`;
            if($('#table-import-errors'))$('#table-import-errors').onclick=()=>downloadExcel(['صف Excel','الخطأ'],result.errors.map(e=>[e.row,e.message]),[0],'import-errors.xlsx').catch(e=>toast(e.message,true));
            $('#table-import-commit').onclick=async()=>{const commit=$('#table-import-commit');commit.disabled=true;try{const state=tableImportState;if(!state)throw Error('أعد المعاينة.');const saved=await api(`/excel/${kind}/commit`,{...state.payload,fingerprint:state.fingerprint});tableImportState=null;$('#modal').close();await show(kind==='choices'?'responses':'riders');toast(`تم استيراد ${saved.count} سجل`);}catch(error){toast(error.message,true);commit.disabled=false;}};
        } catch(error){toast(error.message,true);}finally{button.disabled=false;}
    };
}
document.addEventListener('input',event=>{const input=event.target as HTMLInputElement;if(input.matches('[data-column-filter]'))filterDataGrid(input.closest<HTMLElement>('.data-grid')!);});
document.addEventListener('click',async event=>{
    const button=(event.target as HTMLElement).closest<HTMLElement>('[data-grid-export],[data-grid-clear],[data-grid-csv],[data-table-import]');if(!button)return;
    try {
        if(button.hasAttribute('data-table-import')){await tableImportDialog(button.dataset.tableImport as 'choices'|'riders');return;}
        const grid=button.closest<HTMLElement>('.data-grid')!;
        if(button.hasAttribute('data-grid-csv')){exportGridCsv(grid);return;}
        if(button.hasAttribute('data-grid-clear')){columnSelections.delete(grid.id);grid.querySelectorAll<HTMLInputElement>('[data-column-filter]').forEach(input=>input.value='');filterDataGrid(grid);}
        else await exportDataGrid(grid,button.dataset.gridExport==='filtered');
    }catch(error){toast(error.message,true);}
});

const columnSelections=new Map<string,Map<number,Set<string>>>();
function cellText(row:HTMLTableRowElement,index:number):string{return(row.cells[index]?.textContent||'').trim();}
function columnSelectionMatches(grid:HTMLElement,row:HTMLTableRowElement):boolean{return [...(columnSelections.get(grid.id)||[])].every(([index,values])=>values.has(cellText(row,index)));}
function exportGridCsv(grid:HTMLElement):void{
 const headers=[...grid.querySelectorAll<HTMLElement>('thead>tr:first-child>th')];const indices=headers.map((h,i)=>h.dataset.header?i:-1).filter(i=>i>=0);
 const quote=(v:string)=>'"'+(/^[=+\-@\t\r]/.test(v)?"'"+v:v).replace(/"/g,'""')+'"';
 const rows=[indices.map(i=>headers[i].dataset.header!),...[...grid.querySelectorAll<HTMLTableRowElement>('tbody>tr')].filter(r=>!r.hidden).map(r=>indices.map(i=>cellText(r,i)))];
 const url=URL.createObjectURL(new Blob(['\uFEFF'+rows.map(r=>r.map(quote).join(',')).join('\r\n')],{type:'text/csv;charset=utf-8'}));const a=document.createElement('a');a.href=url;a.download=active+'-filtered.csv';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
}
document.addEventListener('click',event=>{
 const button=(event.target as HTMLElement).closest<HTMLElement>('[data-column-menu]');if(!button)return;
 document.querySelector('.column-popover')?.remove();const grid=button.closest<HTMLElement>('.data-grid')!,index=Number(button.dataset.columnMenu);
 const rows=[...grid.querySelectorAll<HTMLTableRowElement>('tbody>tr')];const distinct=[...new Set(rows.map(r=>cellText(r,index)))].sort(new Intl.Collator('ar',{numeric:true}).compare);
 const selected=columnSelections.get(grid.id)?.get(index);const panel=document.createElement('div');panel.className='column-popover';panel.setAttribute('role','dialog');panel.setAttribute('aria-label','تصفية وترتيب العمود');
 panel.innerHTML=`<div class="actions"><button data-sort="asc">تصاعدي ↑</button><button data-sort="desc">تنازلي ↓</button><button data-close-filter class="ghost" aria-label="إغلاق">×</button></div><input type="search" aria-label="بحث داخل قيم العمود" placeholder="بحث داخل القيم"><div class="actions"><button class="ghost" data-all-values>تحديد الكل</button><button class="ghost" data-no-values>إلغاء الكل</button></div><div class="column-values">${distinct.map((v,i)=>`<label class="check"><input type="checkbox" data-value-index="${i}" ${!selected||selected.has(v)?'checked':''}>${esc(v||'(فارغ)')}</label>`).join('')}</div>`;
 document.body.append(panel);const rect=button.getBoundingClientRect();panel.style.top=Math.min(rect.bottom,innerHeight-330)+'px';panel.style.left=Math.max(8,Math.min(rect.left,innerWidth-280))+'px';
 const apply=()=>{if(!columnSelections.has(grid.id))columnSelections.set(grid.id,new Map());columnSelections.get(grid.id)!.set(index,new Set([...panel.querySelectorAll<HTMLInputElement>('[data-value-index]:checked')].map(x=>distinct[Number(x.dataset.valueIndex)])));filterDataGrid(grid);};
 panel.querySelector('input[type=search]')!.addEventListener('input',e=>{const query=(e.target as HTMLInputElement).value.toLocaleLowerCase();panel.querySelectorAll<HTMLElement>('.column-values label').forEach(l=>l.hidden=!l.textContent!.toLocaleLowerCase().includes(query));});
 panel.addEventListener('change',apply);panel.addEventListener('click',e=>{const target=(e.target as HTMLElement).closest<HTMLElement>('button');if(!target)return;
  if(target.hasAttribute('data-close-filter'))panel.remove();
  if(target.hasAttribute('data-all-values')||target.hasAttribute('data-no-values')){panel.querySelectorAll<HTMLInputElement>('[data-value-index]').forEach(x=>x.checked=target.hasAttribute('data-all-values'));apply();}
  if(target.dataset.sort){const numeric=grid.querySelectorAll<HTMLElement>('thead>tr:first-child>th')[index].dataset.numeric==='true';const compare=new Intl.Collator('ar',{numeric:true}).compare;rows.sort((a,b)=>{const x=cellText(a,index),y=cellText(b,index);return(target.dataset.sort==='asc'?1:-1)*(numeric?(Number(x.replace(/,/g,''))-Number(y.replace(/,/g,''))):compare(x,y));});const body=grid.querySelector('tbody')!;rows.forEach(r=>body.append(r));panel.remove();}
 });
});
document.addEventListener('keydown',e=>{if(e.key==='Escape')document.querySelector('.column-popover')?.remove();});
