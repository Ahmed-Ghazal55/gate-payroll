const themeNames={dark:'داكن',light:'فاتح',dim:'متوسط'};
function setTheme(value:string){const theme=Object.hasOwn(themeNames,value)?value:'light';document.documentElement.dataset.theme=theme;try{localStorage.setItem('gate-theme',theme)}catch{}}
try{setTheme(localStorage.getItem('gate-theme')||'light')}catch{setTheme('light')}
function themeControl(){return `<select class="theme-control" aria-label="مظهر الواجهة" data-theme-select>${options(Object.entries(themeNames),document.documentElement.dataset.theme)}</select>`;}
function visibleView(view:string){if(userRole==='Admin')return true;if(userRole==='Reviewer')return ['responses','riders','analytics'].includes(view);return ['dashboard','riders','payroll','ledger','debts','policies'].includes(view);}
function navIcon(id:string){const paths:Record<string,string>={dashboard:'M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z',riders:'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8M17 4a4 4 0 0 1 0 8M22 21v-2a4 4 0 0 0-3-4',payroll:'M6 3h12v18l-3-2-3 2-3-2-3 2zM9 7h6M9 11h6M9 15h3',forms:'M10 13a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-2 2M14 11a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l2-2',ledger:'M4 3h16v18H4zM8 7h8M8 11h8M8 15h4',users:'M12 3l8 4v6c0 4-8 8-8 8s-8-4-8-8V7zM9 12l2 2 4-4',audit:'M12 8v5l3 2M21 12a9 9 0 1 1-9-9',debts:'M3 7h18v13H3zM3 7l3-4h12l3 4M15 11h6v5h-6z',imports:'M12 16V3M7 8l5-5 5 5M4 15v6h16v-6',policies:'M4 6h16M4 12h16M4 18h16',responses:'M5 12l4 4L19 6'};return `<svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${paths[id]||paths.forms}"/></svg>`;}
document.addEventListener('change',e=>{const target=e.target as HTMLSelectElement;if(target.matches('[data-theme-select]'))setTheme(target.value);});
const fieldInput=(label:string,key:string,value='',type='text',required=false)=>`<label>${esc(label)}<input name="${key}" type="${type}" value="${esc(value)}" ${required?'required':''}></label>`;
const roleLabels={Admin:'مسؤول النظام',Accountant:'محاسب',Reviewer:'مراجع'};
const statusLabels={Active:'نشط',OnLeave:'في إجازة',Archived:'مؤرشف',NeedsReview:'يحتاج مراجعة',active:'نشط',suspended:'موقوف',settled:'مسدد',paused:'متوقف',pending_approval:'بانتظار تصديق القسم الآخر',cancelled:'ملغي'};
const allocationLabels:Record<string,string>={RiderFull:'تحمل المندوب 100% (ذمة مدينة)',CompanyFull:'تحمل الشركة 100% (مصروف)',SharedSplit:'تحمل جزئي مشترك (شركة + مندوب)'};

let financeSubTab='debts-list';
let statementRiderIqama='';
let statementSponsorFilter='';

async function enterpriseView(view:string):Promise<string>{
 if(view==='riders'){
  const [riders,policies,quality]=await Promise.all([
   api('/riders'),
   userRole==='Reviewer'?Promise.resolve([]):api('/policies'),
   api('/riders/data-quality').catch(()=>({totalRiders:0,localBaselineAvailable:false,missingPhoneCount:0,missingCityCount:0,missingSponsorCount:0,missingIbanCount:0,needsReviewCount:0}))
  ]);
  cache.riders=riders;cache.policies=policies;cache.riderQuality=quality;
  const kpiStrip=`<div class="stats" style="margin-bottom:14px">
   <div class="stat"><div class="label">إجمالي المناديب المسجلين</div><strong class="number">${riders.length}</strong><div class="hint">${quality.localBaselineAvailable?'الدليل المرجعي المدمج (826 مندوب) متاح للمزامنة':'هوية فعلية واحدة لكل مندوب'}</div></div>
   <div class="stat"><div class="label">نواقص الجوال والمدينة</div><strong class="number ${quality.missingPhoneCount+quality.missingCityCount>0?'money-negative':'money-positive'}">${quality.missingPhoneCount} جوال · ${quality.missingCityCount} مدينة</strong><div class="hint">قابلة للاستكمال التلقائي من المصادر</div></div>
   <div class="stat"><div class="label">نواقص الكفيل والآيبان</div><strong class="number">${quality.missingSponsorCount} كفيل · ${quality.missingIbanCount} آيبان</strong><div class="hint">مربوطة بالسجلات التجارية الثلاثة</div></div>
   <div class="stat"><div class="label">حالات تحتاج مراجعة</div><strong class="number ${quality.needsReviewCount>0?'money-negative':'money-positive'}">${quality.needsReviewCount}</strong><div class="hint">مناديب مستوردون أو قيد استكمال الملف</div></div>
  </div>`;
  return heading(
   'دليل المناديب وملفاتهم الوثائقية 360°',
   'هوية فعلية واحدة، مع الفصل بين الكفالة والعلاقة التشغيلية والسياسة المالية، واستكمال آلي للبيانات المفقودة وأرشيف الوثائق السبع.',
   userRole==='Admin'
    ? `<button data-action="rider-new">＋ إضافة مندوب</button>${excelControls('riders')}<button class="secondary" data-enterprise="rider-enrich">⚡ جلب واستكمال البيانات المفقودة</button><button class="secondary" data-enterprise="baseline">مزامنة الدليل المرجعي (826 مندوب)</button>`
    : ''
  )+kpiStrip+`<div class="panel">${table(
   ['المندوب','الإقامة','الجنسية','الجوال','العلاقة','الكفيل','المدينة','نوع المركبة','رخصة القيادة','الحالة','سياسة الحساب','الرد المختار',''],
   riders.map(r=>`<tr>
    <td><strong>${esc(r.name)}</strong></td>
    <td class="number identifier">${esc(r.iqama)}</td>
    <td>${esc(r.nationality||'—')}</td>
    <td dir="ltr" class="number">${esc(r.phone||'—')}</td>
    <td>${esc({SponsoredInternal:'كفالة الشركة',External:'خارجي'}[r.engagementType]||'غير محسوم')}</td>
    <td>${esc(r.sponsor||'—')}</td>
    <td>${esc(r.city||'—')}</td>
    <td>${esc({Car:'سيارة',Motorcycle:'دباب'}[r.operationalRole]||'غير محدد')}</td>
    <td>${esc(r.drivingLicense||'—')}</td>
    <td>${badge(statusLabels[r.status]||r.status,r.status==='Active'?'good':r.status==='NeedsReview'?'warn':'')}</td>
    <td>${esc(policies.find(p=>p.id===r.policyId)?.name||'—')}</td>
    <td>${esc(systems[r.workPreference]||r.workPreference||'لم يختر بعد')}</td>
    <td><div class="row-actions">
     ${userRole!=='Reviewer'?`<button class="small secondary" data-enterprise="rider-profile-360" data-iqama="${esc(r.iqama)}">📂 الملف والوثائق 360°</button>
     <button class="small ghost" data-enterprise="open-statement" data-iqama="${esc(r.iqama)}">📊 كشف الحساب</button>`:''}
     ${userRole==='Admin'?`<button class="small ghost" data-action="rider-edit" data-id="${esc(r.id)}">تعديل</button>`:''}
    </div></td>
   </tr>`)
  )}</div>`;
 }
 if(view==='forms'){const forms=await api('/campaigns');cache.campaigns=forms;return heading('النماذج والروابط','روابط محفوظة قابلة للنسخ وQR دون إظهار بيانات المناديب.',`<button data-action="campaign-new">إنشاء حملة اختيار</button><button class="secondary" data-view="builder">مصمم النماذج</button>`)+`<div id="new-link"></div><div class="panel">${table(['الحملة','الانتهاء','الحالة','الرابط',''],forms.map(f=>`<tr><td>${esc(f.title)}</td><td>${esc(f.expiresAt.slice(0,10))}</td><td>${esc(f.status)}</td><td><span class="link-label" dir="ltr">${esc(f.publicUrl)}</span></td><td><button data-enterprise="copy" data-url="${esc(f.publicUrl)}">نسخ الرابط</button><button class="secondary" data-enterprise="qr" data-url="${esc(f.qrCodeUrl)}">QR</button><button class="secondary" data-enterprise="campaign-edit" data-id="${esc(f.id)}">تعديل النموذج</button>${userRole==='Admin'?`<button class="danger" data-enterprise="campaign-delete" data-id="${esc(f.id)}">حذف النموذج</button>`:''}${f.status==='open'?`<button class="ghost" data-action="campaign-close" data-id="${f.id}">إغلاق</button>`:''}</td></tr>`))}</div>`;}
 if(view==='users'){const users=await api('/users');cache.users=users;return heading('إدارة المستخدمين','الصلاحيات تُفحص في الخادم، وإيقاف الحساب يلغي جلساته.',`<button data-enterprise="user-new">إنشاء مستخدم</button>`)+`<div class="panel">${table(['اسم المستخدم','الدور','الحالة','تاريخ الإنشاء','آخر دخول',''],users.map(u=>`<tr><td>${esc(u.username)}</td><td>${esc(roleLabels[u.role])}</td><td>${esc(statusLabels[u.status])}</td><td>${esc(u.createdAt)}</td><td>${esc(u.lastLoginAt)}</td><td><button class="secondary" data-enterprise="user-edit" data-id="${u.id}">تعديل الصلاحيات</button></td></tr>`))}</div>`;}
 if(view==='debts')return await renderFinancialSystemHub();
 if(view==='ledger'){cache.riders=await api('/riders');const rows=await api('/ledger');return heading('دفتر الحسابات','حركات المسيرات المعتمدة والأقساط والدفعات، دون إدخال مباشر يتجاوز الاعتماد.',`<button data-enterprise="inquiry-code">رمز استعلام للمندوب</button>`)+`<div class="panel">${table(['الإقامة','الفترة','الحركة','المبلغ','المرجع'],rows.map(x=>`<tr><td>${esc(x.iqama)}</td><td>${esc(x.period)}</td><td>${esc(x.kind)}</td><td class="number">${money(x.amount)}</td><td>${esc(x.sourceRef)}</td></tr>`))}</div>`;}
 if(view==='builder'){const forms=await api('/forms/builder');cache.customForms=forms;return heading('مصمم النماذج','أسئلة بثلاث لغات؛ النشر يثبّت المخطط ويحفظ رابط المشاركة.',`<button data-enterprise="builder-new">نموذج جديد</button>`)+`<div class="panel">${table(['العنوان','الحالة','الحد الأقصى','الانتهاء',''],forms.map(f=>`<tr><td>${esc(f.titleAr)}</td><td>${esc(f.status)}</td><td>${f.maxSubmissions}</td><td>${esc(f.expiresAt.slice(0,10))}</td><td>${f.status==='draft'?`<button data-enterprise="builder-publish" data-id="${f.id}">نشر</button><button class="secondary" data-enterprise="builder-edit" data-id="${f.id}">تحرير</button>`:`<button data-enterprise="copy" data-url="${esc(f.publicUrl)}">نسخ الرابط</button><button class="secondary" data-enterprise="qr" data-url="${esc(f.qrCodeUrl)}">QR</button><button class="secondary" data-enterprise="analytics" data-id="${f.id}">التحليلات</button>${f.status==='open'?`<button class="ghost" data-enterprise="builder-close" data-id="${f.id}">إغلاق</button>`:''}`}<button class="danger" data-enterprise="builder-delete" data-id="${esc(f.id)}">حذف النموذج</button></td></tr>`))}</div>`;}
 if(view==='analytics')return await formAnalyticsView();
 return '';
}

async function renderFinancialSystemHub():Promise<string>{
 const [debts,riders,catalog,sponsors,accounts,journals,approvals,notifCenter,broadcasts,promissory]=await Promise.all([
  api('/debts'),api('/riders'),api('/finance/catalog'),api('/finance/sponsors'),api('/finance/accounts'),
  api('/finance/journals'),api('/approvals'),api('/notifications/center'),api('/rider-mobile/broadcasts'),api('/promissory-notes')
 ]);
 cache.debts=debts;cache.riders=riders;cache.financeCatalog=catalog;cache.sponsors=sponsors;cache.accounts=accounts;
 cache.journals=journals;cache.approvals=approvals;cache.notifCenter=notifCenter;cache.broadcasts=broadcasts;cache.promissory=promissory;
 if(!statementRiderIqama&&riders.length)statementRiderIqama=riders[0].iqama;

 const pendingApprovalsCount=approvals.filter((a:any)=>a.status==='pending').length;
 const totalActiveDebt=debts.filter((d:any)=>d.status==='active'||d.status==='paused').reduce((s:number,d:any)=>s+Number(d.remainingBalance||0),0);
 const totalPaidDebt=debts.reduce((s:number,d:any)=>s+Number(d.paidAmount||0),0);
 const totalWaivedDebt=debts.reduce((s:number,d:any)=>s+Number(d.waivedAmount||0),0);

 const subTabs=[
  ['debts-list',`المديونيات والسلف وتكاليف HR (${debts.length})`],
  ['vaults-coa',`الخزائن وشجرة الحسابات والقيود (${journals.length})`],
  ['statement-360','كشف حساب المندوب والشركات 360°'],
  ['approvals-notif',`الاعتمادات المتبادلة والإشعارات والجوال ${pendingApprovalsCount?`(${pendingApprovalsCount})`:''}`],
  ['promissory-archive',`أرشيف سندات الأمر ${promissory.featureEnabled?'(مفعل)':'(معلق)'}`]
 ];

 const topActions=userRole==='Reviewer'
  ? `<button class="secondary" data-enterprise="inquiry-code">رمز استعلام للمندوب</button><a class="button secondary" href="/rider-app" target="_blank">📱 بوابة تطبيق المندوب</a>`
  : `<button data-enterprise="debt-new">＋ تسجيل مديونية / خدمة HR / سلفة</button>
     <button class="good" data-enterprise="opening-debt-import">📥 رفع رصيد أول المدة وشيت المديونيات</button>
     <button class="secondary" data-enterprise="bulk-collect">شيت التحصيل المجمع (Excel / مباشر)</button>
     <button class="secondary" data-enterprise="vault-transfer">تحويل بين الصناديق والبنوك</button>
     <button class="secondary" data-enterprise="hr-advance-print">نموذج سلفة نقدية / سند قبض</button>`;

 const sponsorCards=sponsors.map((sp:any)=>`
  <article class="stat">
   <div class="label">${esc(sp.shortNameAr)} — <span class="number">${esc(sp.unifiedNumber)}</span></div>
   <strong class="money-positive">${money(sp.receivablesBalance)} <small style="font-size:12px">ر.س ذمم مدينة</small></strong>
   <div class="hint">رصيد الصناديق والبنوك: <b class="number">${money(sp.vaultBalance)}</b> ر.س · مديونيات نشطة: <b>${sp.activeDebtsCount}</b> · مصروفات وتحملات: <b class="number">${money(sp.expensesTotal)}</b> ر.س · جاري السجلات: <b class="number">${money(sp.interCompanyNet)}</b> ر.س</div>
  </article>`).join('');

 let bodyHtml='';
 if(financeSubTab==='debts-list'){
  bodyHtml=`
   <div class="stats">${sponsorCards}
    <article class="stat">
     <div class="label">إجمالي المحصل والإعفاءات الإدارية</div>
     <strong>${money(totalPaidDebt)} <small style="font-size:12px">ر.س محصل</small></strong>
     <div class="hint">متبقي نشط: <b class="number">${money(totalActiveDebt)}</b> ر.س · إعفاءات إدارية: <b class="number">${money(totalWaivedDebt)}</b> ر.س</div>
    </article>
   </div>
   <div class="notice">
    <strong>القاعدة المحاسبية المطبقة:</strong> الذمم المدينة (<code>1210/1220</code>) وحسابات المصروفات (<code>5101..5203</code>) والصناديق (<code>MAIN/SUP/BANK</code>) طبيعتها <b>مدينة (Debit)</b> تزيد في الطرف المدين وتنقص في الطرف الدائن. يمكنك تحصيل مديونية أي مندوب نقداً أو بنكياً على أي سجل تجاري أو شراء/ترحيل المديونية بين السجلات مع تسوية آلية في <b>جاري السجلات الشقيقة (1310)</b>.
   </div>
   <div class="panel">
    <div class="panel-title">
     <h2>سجل المديونيات، خدمات الموارد البشرية (HR)، السلف، والأقساط</h2>
     <div class="actions"><a class="button secondary" href="/api/debts/bulk-collect/template">تنزيل قالب التحصيل المجمع (Excel)</a></div>
    </div>
    ${table(
     ['المندوب / الموظف','الإقامة','البند / الخدمة','جهة التحمل','إجمالي التكلفة','حصة الشركة (مصروف)','ذمة المندوب','المحصل','إعفاء إداري','المتبقي','القسط','السجل الدائن','الصندوق','القيد','الحالة','الإجراءات'],
     debts.map((d:any)=>{
      const rName=d.riderName||riders.find((r:any)=>r.iqama===d.iqama)?.name||d.iqama;
      const catAr=d.categoryAr||(catalog.categories||[]).find((c:any)=>c.code===d.category)?.nameAr||d.category;
      const canAct=userRole!=='Reviewer'&&Number(d.remainingBalance)>0&&(d.status==='active'||d.status==='paused');
      return `<tr>
       <td><strong>${esc(rName)}</strong><div class="muted small">${d.debtorType==='Employee'?'موظف إداري':'مندوب'}</div></td>
       <td class="number identifier">${esc(d.iqama)}</td>
       <td><strong>${esc(catAr)}</strong><div class="muted small">${esc(d.sourceRef||'')}</div></td>
       <td>${badge(allocationLabels[d.allocationMode||'RiderFull']||'تحمل المندوب',d.allocationMode==='CompanyFull'?'good':d.allocationMode==='SharedSplit'?'warn':'')}</td>
       <td class="number">${money(d.totalCostAmount??d.totalAmount)}</td>
       <td class="number">${money(d.companyShareAmount||0)}</td>
       <td class="number">${money(d.totalAmount)}</td>
       <td class="number money-positive">${money(d.paidAmount||0)}</td>
       <td class="number">${money(d.waivedAmount||0)}</td>
       <td class="number ${Number(d.remainingBalance)>0?'money-negative':''}"><strong>${money(d.remainingBalance)}</strong></td>
       <td class="number">${money(d.monthlyInstallment)}</td>
       <td>${esc(d.creditorSponsorNameAr||'غير مربوط — راجع الجدولة')}</td>
       <td class="number">${esc(d.paymentVaultCode||'غير مسجل')}</td>
       <td class="number">${esc(d.journalNumber||'—')}</td>
       <td>${badge(statusLabels[d.status]||d.status,d.status==='settled'?'good':d.status==='pending_approval'?'warn':'')}</td>
       <td>
        <div class="row-actions">
         ${canAct?`<button class="small good" data-enterprise="debt-collect" data-id="${esc(d.id)}">تحصيل / إعفاء</button>
                   <button class="small secondary" data-enterprise="debt-edit" data-id="${esc(d.id)}">جدولة</button>
                   <button class="small secondary" data-enterprise="debt-transfer-sp" data-id="${esc(d.id)}">شراء/نقل لسجل</button>`:''}
         <button class="small ghost" data-enterprise="open-statement" data-iqama="${esc(d.iqama)}">كشف 360°</button>
         <button class="small ghost" data-enterprise="print-voucher" data-id="${esc(d.id)}">سند/نموذج</button>
        </div>
       </td>
      </tr>`;
     })
    )}
   </div>`;
 } else if(financeSubTab==='vaults-coa'){
  bodyHtml=`
   <div class="stats">${sponsorCards}</div>
   <div class="panel">
    <div class="panel-title">
     <h2>شجرة الحسابات الجزئية، الصناديق الداخلية (MAIN)، كاش جاهز (SUP)، البنوك (BANK)، وحسابات مصروفات HR</h2>
     <button class="secondary" data-enterprise="vault-transfer">إيداع / تحويل بين الخزائن والبنوك</button>
    </div>
    ${table(
     ['رمز الحساب','اسم الحساب / الصندوق','النوع','الطبيعة المحاسبية','السجل الافتراضي','إجمالي المدين (Debit)','إجمالي الدائن (Credit)','الرصيد الصافي','قاعدة الزيادة والنقصان'],
     accounts.map((a:any)=>`<tr>
      <td class="number"><strong>${esc(a.code)}</strong></td>
      <td><strong>${esc(a.nameAr)}</strong>${a.isVault?` ${badge('خزينة/بنك','good')}`:''}</td>
      <td>${esc(a.accountType)}</td>
      <td>${badge(a.naturalSideAr,a.naturalSide==='Debit'?'':'warn')}</td>
      <td>${esc(a.defaultSponsorNameAr)}</td>
      <td class="number">${money(a.debitTotal)}</td>
      <td class="number">${money(a.creditTotal)}</td>
      <td class="number ${a.netBalance<0?'money-negative':'money-positive'}"><strong>${money(a.netBalance)}</strong></td>
      <td class="small muted">${esc(a.ruleExplanationAr)}</td>
     </tr>`)
    )}
   </div>
   <div class="panel">
    <h2>دفتر القيود المحاسبية المزدوجة (Double-Entry Journal Entries)</h2>
    ${table(
     ['رقم القيد','التاريخ','الفترة','نوع المعاملة','البيان','السجل المنشئ','السجل الدائن','إجمالي المدين','إجمالي الدائن','المرجع','التفاصيل'],
     journals.map((j:any)=>`<tr>
      <td class="number"><strong>${esc(j.journalNumber)}</strong></td>
      <td class="number">${esc(j.date)}</td>
      <td class="number">${esc(j.period)}</td>
      <td>${esc(j.transactionType)}</td>
      <td>${esc(j.description)}</td>
      <td>${esc(j.originSponsorNameAr)}</td>
      <td>${esc(j.creditorSponsorNameAr)}</td>
      <td class="number money-positive">${money(j.totalDebit)}</td>
      <td class="number">${money(j.totalCredit)}</td>
      <td>${esc(j.sourceRef)}</td>
      <td><button class="small secondary" data-enterprise="journal-detail" data-id="${esc(j.id)}">عرض الأسطر والتفسير</button></td>
     </tr>`)
    )}
   </div>`;
 } else if(financeSubTab==='statement-360'){
  const st=statementRiderIqama?await api(`/finance/statement/rider/${encodeURIComponent(statementRiderIqama)}${statementSponsorFilter?`?sponsorId=${encodeURIComponent(statementSponsorFilter)}`:''}`):null;
  cache.currentStatement=st;
  bodyHtml=`
   <div class="panel">
    <div class="form-grid">
     <label>اختر المندوب أو الموظف لعرض كشف الحساب الشامل 360°
      <select id="statement-rider-select">${options(riders.map((r:any)=>[r.iqama,`${r.name} — ${r.iqama} (${r.sponsor||'البوابا'})`]),statementRiderIqama)}</select>
     </label>
     <label>تصفية حسب السجل التجاري (اختياري)
      <select id="statement-sponsor-select">
       <option value="">كل السجلات التجارية (البوابا + إكسبرس + التجارية)</option>
       ${options(sponsors.map((s:any)=>[s.id,`${s.shortNameAr} — ${s.nameAr}`]),statementSponsorFilter)}
      </select>
     </label>
    </div>
   </div>
   ${st?`
   <div class="stats">
    <article class="stat"><div class="label">إجمالي أصل المديونيات المسجلة (مدين)</div><strong class="money-negative">${money(st.totalPrincipalDebt)}</strong><div class="hint">الكفالة: ${esc(st.sponsorshipSponsor)}</div></article>
    <article class="stat"><div class="label">إجمالي المسدد نقداً وبنكياً ومن الراتب (دائن)</div><strong class="money-positive">${money(st.totalPaidAmount)}</strong><div class="hint">تخفيض مباشر للذمة المدينة</div></article>
    <article class="stat"><div class="label">إجمالي الإعفاءات الإدارية المعتمدة (دائن)</div><strong>${money(st.totalWaivedAmount)}</strong><div class="hint">تُقيد في ح/ مصروف إعفاءات 5202</div></article>
    <article class="stat"><div class="label">صافي المديونية المتبقية حالياً</div><strong class="${st.totalRemainingDebt>0?'money-negative':'money-positive'}">${money(st.totalRemainingDebt)}</strong><div class="hint">رصيد دفتر الرواتب العام: ${money(st.payrollLedgerBalance)} ر.س</div></article>
   </div>
   <div class="panel">
    <div class="panel-title">
     <h2>توزيع ذمم المندوب (${esc(st.riderName)} — ${esc(st.iqama)}) حسب السجلات التجارية</h2>
     <div class="actions">
      <button class="secondary no-print" data-action="print">طباعة كشف الحساب الرسمي</button>
      <button class="secondary no-print" data-enterprise="share-statement-mobile" data-iqama="${esc(st.iqama)}">مشاركة الكشف داخل بوابة المندوب</button>
     </div>
    </div>
    ${table(
     ['السجل التجاري','إجمالي أصل الدين (مدين)','المسدد (دائن)','الإعفاء الإداري (دائن)','الرصيد المتبقي للسجل'],
     st.bySponsor.map((b:any)=>`<tr>
      <td><strong>${esc(b.sponsorNameAr)}</strong></td>
      <td class="number">${money(b.totalPrincipal)}</td>
      <td class="number money-positive">${money(b.totalPaid)}</td>
      <td class="number">${money(b.totalWaived)}</td>
      <td class="number ${b.remainingBalance>0?'money-negative':''}"><strong>${money(b.remainingBalance)}</strong></td>
     </tr>`)
    )}
   </div>
   <div class="panel">
    <h2>كشف حركة المديونية التفصيلي (مدين / دائن / رصيد تراكمي)</h2>
    ${table(
     ['التاريخ','الفترة','نوع الحركة','البند / الصندوق','السجل التجاري','رقم المرجع / السند','البيان','مدين (+ زيادة دين)','دائن (- سداد / إعفاء)','الرصيد التراكمي'],
     st.statementLines.map((l:any)=>`<tr>
      <td class="number">${esc(l.date)}</td>
      <td class="number">${esc(l.period)}</td>
      <td>${badge(l.typeAr,l.credit>0?'good':'warn')}</td>
      <td>${esc(l.categoryAr)}</td>
      <td>${esc(l.sponsorNameAr)}</td>
      <td class="number">${esc(l.reference)}</td>
      <td>${esc(l.description)}</td>
      <td class="number ${l.debit>0?'money-negative':''}">${l.debit>0?money(l.debit):'—'}</td>
      <td class="number ${l.credit>0?'money-positive':''}">${l.credit>0?money(l.credit):'—'}</td>
      <td class="number"><strong>${money(l.runningBalance)}</strong></td>
     </tr>`)
    )}
   </div>`:empty('اختر مندوباً','اختر مندوباً من القائمة أعلاه لعرض كشف حسابه التفصيلي.')}`;
 } else if(financeSubTab==='approvals-notif'){
  bodyHtml=`
   <div class="split">
    <div class="panel">
     <div class="panel-title">
      <h2>طابور الاعتمادات المتبادلة (المالية ⇄ HR ⇄ الحركة)</h2>
      ${badge(`${pendingApprovalsCount} بانتظار القرار`,pendingApprovalsCount?'warn':'good')}
     </div>
     <p class="muted small">عند تسجيل تحصيل أو مديونية مشروطة من HR يُشترط تصديق المالية، وعند تسجيلها من المالية يُشترط تصديق HR مع توثيق الرتبة والسبب.</p>
     ${table(
      ['العنوان','من قسم','إلى قسم','الرتبة المطلوبة','الحالة','ملاحظة القرار','الإجراء'],
      approvals.map((a:any)=>`<tr>
       <td><strong>${esc(a.titleAr)}</strong><div class="muted small">${esc(a.bodyAr)}</div></td>
       <td>${esc(a.fromDepartment)}</td>
       <td>${esc(a.targetDepartment)}</td>
       <td>رتبة ${esc(a.requiredRankLevel)}</td>
       <td>${badge(a.status==='approved'?'مصدق':a.status==='rejected'?'مرفوض':a.status==='suspended'?'معلق':'بانتظار التصديق',a.status==='approved'?'good':a.status==='rejected'?'bad':'warn')}</td>
       <td>${esc(a.decisionNote||'—')}</td>
       <td>${a.status==='pending'||a.status==='suspended'?`<button class="small good" data-enterprise="approval-decide" data-id="${esc(a.id)}">قرار وتصديق</button>`:esc(a.decidedBy||'')}</td>
      </tr>`)
     )}
    </div>
    <div class="panel">
     <div class="panel-title">
      <h2>مركز الإشعارات والمهام (To-Do) وتطبيق الجوال (Google Play / FCM)</h2>
      <div class="actions">
       <a class="button secondary" href="/rider-app" target="_blank">📱 فتح تطبيق المندوب (/rider-app)</a>
       ${userRole==='Admin'?`<button class="secondary" data-enterprise="firebase-config">⚙️ إعدادات ربط Firebase و Google Play</button>`:''}
       <button class="secondary" data-enterprise="notif-new">＋ إشعار / مهمة لقسم</button>
       <button data-enterprise="mobile-broadcast">📲 إرسال إشعار لتطبيق المناديب</button>
      </div>
     </div>
     ${table(
      ['التبويب','القسم المستهدف','الرتبة','العنوان والرسالة','الحالة',''],
      (notifCenter.items||[]).map((n:any)=>`<tr>
       <td>${badge(n.tab==='todo'?'مهمة To-Do':n.tab==='approvals'?'اعتماد':'إشعار عام',n.tab==='approvals'?'warn':'')}</td>
       <td>${esc(n.targetDepartment)}</td>
       <td>رتبة ${esc(n.requiredRankLevel||1)}</td>
       <td><strong>${esc(n.titleAr)}</strong><div class="muted small">${esc(n.bodyAr)}</div></td>
       <td>${badge(n.completed||n.status==='acknowledged'?'تم الإنجاز / العلم':'غير مقروء',n.completed||n.status==='acknowledged'?'good':'warn')}</td>
       <td><button class="small secondary" data-enterprise="toggle-todo" data-id="${esc(n.id)}">${n.completed?'إعادة فتح':'تأكيد الإنجاز ✓'}</button></td>
      </tr>`)
     )}
     <h3 style="margin-top:18px">سجل رسائل وإشعارات تطبيق الجوال (Google Play FCM)</h3>
     ${table(
      ['التصنيف','المستهدف','العنوان','الرابط المرفق','قناة الإرسال','التاريخ'],
      broadcasts.map((b:any)=>`<tr>
       <td>${esc(b.category)}</td>
       <td class="number">${esc(b.targetIqama)}</td>
       <td><strong>${esc(b.titleAr)}</strong><div class="muted small">${esc(b.bodyAr)}</div></td>
       <td dir="ltr" class="small">${esc(b.actionUrl||'—')}</td>
       <td>${badge(b.deliveryChannel,'good')}</td>
       <td class="small">${esc((b.createdAt||'').slice(0,16))}</td>
      </tr>`)
     )}
    </div>
   </div>`;
 } else if(financeSubTab==='promissory-archive'){
  bodyHtml=`
   <div class="panel">
    <div class="panel-title">
     <h2>أرشيف سندات الأمر الموحد (منصة نافذ — الحركة · الموارد البشرية · المنصات)</h2>
     <div class="actions">
      ${userRole==='Admin'?`<button class="secondary" data-enterprise="toggle-promissory-flag" data-enabled="${promissory.featureEnabled?'false':'true'}">${promissory.featureEnabled?'تعليق الموديول (Dormant Mode)':'تفعيل الموديول للاختبار'}</button>`:''}
      ${promissory.featureEnabled?`<button data-enterprise="promissory-new">＋ أرشفة سند لأمر جديد</button>`:''}
     </div>
    </div>
    <div class="notice ${promissory.featureEnabled?'':'warn'}">
     ${promissory.featureEnabled
      ? 'الموديول مفعل حالياً: يمكنك أرشفة سندات الأمر وربطها بالسجل التجاري المستفيد ومسار المرفقات المتوافق مع نظام البوابة (<code>wwwroot/private/riders/{riderId}/promissory-notes/</code>).'
      : '<strong>وضع التعليق الاختياري (Dormant by Default):</strong> تم تجهيز الجداول والـ Endpoints والواجهة بالكامل للتوافق مع أرشيف سندات الأمر في نظام البوابة، مع إبقائه معلقاً افتراضياً حسب توجيهكم حتى وقت الدمج أو التفعيل بضغطة زر.'}
    </div>
    ${table(
     ['رقم السند / نافذ','الإقامة','اسم المدين','القسم المختص','السجل التجاري المستفيد','المبلغ','تاريخ الإصدار','الحالة القانونية','مسار الأرشيف'],
     (promissory.items||[]).map((p:any)=>`<tr>
      <td class="number"><strong>${esc(p.noteNumber)}</strong></td>
      <td class="number">${esc(p.iqama)}</td>
      <td>${esc(p.debtorName)}</td>
      <td>${esc(p.department)}</td>
      <td>${esc(p.beneficiarySponsorNameAr)}</td>
      <td class="number"><strong>${money(p.amount)}</strong></td>
      <td class="number">${esc(p.issueDate)}</td>
      <td>${badge(p.status,p.status==='Cleared'?'good':'warn')}</td>
      <td dir="ltr" class="small muted">${esc(p.attachmentPath)}</td>
     </tr>`)
    )}
   </div>`;
 }

 return heading(
  'منظومة إدارة المديونيات والنظام المالي والخزائن',
  'شجرة حسابات جزئية، فصل كامل لذمم السجلات التجارية الثلاثة، توزيع ذكي لتكاليف HR، تحصيل مرن وإعفاءات إدارية، واعتمادات متبادلة.',
  topActions
 ) + `${catalog.unassignedDebtCount?`<div class="notice warn">${catalog.unassignedDebtCount} مديونية قديمة دون سجل تجاري مراجع؛ لا توزع على السجلات تلقائيًا. يستطيع المسؤول ربطها من الجدولة.</div>`:''}`+`<div class="response-chips" role="tablist" aria-label="أقسام النظام المالي والمديونيات">
  ${subTabs.map(([id,label])=>`<button class="small ${financeSubTab===id?'':'secondary'}" data-enterprise="finance-subtab" data-subtab="${id}">${label}</button>`).join('')}
 </div>` + bodyHtml;
}

function bindEnterprise(view:string){
 if(view==='debts'){
  const rSel=$('#statement-rider-select');
  if(rSel)rSel.onchange=async()=>{statementRiderIqama=rSel.value;await show('debts');};
  const sSel=$('#statement-sponsor-select');
  if(sSel)sSel.onchange=async()=>{statementSponsorFilter=sSel.value;await show('debts');};
  return;
 }
 if(view!=='analytics')return;
 const select=$('#analytics-campaign-select');
 select.onchange=async()=>{analyticsSelection=select.value;try{await updateAnalytics()}catch(error){toast(error.message,true)}};
 let busy=false;
 const timer=setInterval(async()=>{if(active!=='analytics'||!document.querySelector('#analytics-campaign-select')){clearInterval(timer);return;}if(busy)return;busy=true;try{await updateAnalytics()}catch{}finally{busy=false}},10000);
}

function riderDialog(id?:string){const r=cache.riders?.find(x=>x.id===id)||{};modal(id?'تعديل بيانات المندوب':'إضافة مندوب',`<form id="rider-form" class="form-grid">${fieldInput('الإقامة','iqama',r.iqama||'','text',true)}${fieldInput('الاسم المصدري','name',r.name||'','text',true)}${fieldInput('الجنسية','nationality',r.nationality)}${fieldInput('الجوال الدولي','phone',r.phone)}${fieldInput('الكفيل / السجل التجاري','sponsor',r.sponsor||'شركة البوابة المقبلة للتجارة')}${fieldInput('المدينة','city',r.city)}${fieldInput('رخصة القيادة','drivingLicense',r.drivingLicense||'')}${fieldInput('المنصات العاملة','platforms',r.platforms||'')}${fieldInput('رقم الآيبان البنكي (IBAN)','iban',r.iban||'')}<label>نوع العلاقة<select name="engagementType">${options([['','غير محسوم'],['SponsoredInternal','كفالة الشركة'],['External','خارجي']],r.engagementType)}</select></label><label>نوع المركبة<select name="operationalRole">${options([['','غير محدد'],['Car','سيارة'],['Motorcycle','دباب']],r.operationalRole)}</select></label><label>الحالة<select name="status">${options(['Active','OnLeave','Archived','NeedsReview'].map(x=>[x,statusLabels[x]]),r.status||'NeedsReview')}</select></label><label>سياسة الحساب<select name="policyId"><option value="">لم تحدد</option>${options((cache.policies||[]).filter(p=>p.status==='approved').map(p=>[p.id,p.name]),r.policyId)}</select></label><button>حفظ</button></form>`);if(id)$('#rider-form [name=iqama]').readOnly=true;$('#rider-form').onsubmit=submit(async d=>{await api('/riders',d);$('#modal').close();await show('riders');});}
function userDialog(id?:string){const u=cache.users?.find(x=>x.id===id)||{};modal(id?'صلاحيات المستخدم':'مستخدم جديد',`<form id="user-form" class="form-grid">${id?'':fieldInput('اسم المستخدم','username','','text',true)+fieldInput('كلمة مرور12حرفًا على الأقل','password','','password',true)}<label>الدور<select name="role">${options(Object.entries(roleLabels),u.role||'Reviewer')}</select></label>${id?`<label>الحالة<select name="status">${options([['active','نشط'],['suspended','موقوف']],u.status)}</select></label>`:''}<button>حفظ</button></form>`);$('#user-form').onsubmit=submit(async d=>{await api(id?`/users/${id}/status`:'/users',d,id?'PUT':'POST');$('#modal').close();await show('users');});}

function debtDialog(id?:string){
 const d=cache.debts?.find((x:any)=>x.id===id)||{};
 const catalog=cache.financeCatalog||{categories:[],sponsors:[],vaults:[]};
 const catOptions=(catalog.categories||[]).length
  ? catalog.categories.map((c:any)=>[c.code,`${c.nameAr} (${c.sector}) — ح/${c.expenseAccountCode}`])
  : ['Advance','TrafficFine','EquipmentLoan','VehicleInstallment','AdministrativeDeduction'].map(x=>[x,x]);
 const spOptions=(catalog.sponsors||[]).map((s:any)=>[s.id,`${s.shortNameAr} — ${s.nameAr} (${s.unifiedNumber})`]);
 const vaultOptions=(catalog.vaults||[]).map((v:any)=>[v.code,`${v.code} — ${v.nameAr}`]);

 modal(id?'إعادة جدولة وتحديث حالة المديونية':'تسجيل مديونية / خدمة HR / سلفة جديدة',`<form id="debt-form" class="form-grid">
  ${!id?`
   <label>المندوب / الموظف<select name="iqama" required>${riderOptions()}</select></label>
   <label>تصنيف المدين<select name="debtorType"><option value="Rider">مندوب توصيل (ذمم 1210)</option><option value="Employee">موظف إداري (ذمم 1220)</option></select></label>
   <label class="full">البند أو خدمة الـ HR أو السلفة<select name="category" id="debt-category-select">${options(catOptions)}</select></label>
   <label>آلية التحمل وتوزيع التكلفة
    <select name="allocationMode" id="debt-allocation-mode">
     <option value="RiderFull">تحمل المندوب/الموظف بالكامل 100% (ذمة مدينة)</option>
     <option value="SharedSplit">تحمل جزئي مشترك (جزء مصروف على الشركة + جزء ذمة على المندوب)</option>
     <option value="CompanyFull">تحمل الشركة بالكامل 100% (مصروف خالص دون مديونية)</option>
    </select>
   </label>
   <label>القسم المنشئ للطلب
    <select name="initiatingDepartment">
     <option value="HR">الموارد البشرية والعلاقات الحكومية (HR)</option>
     <option value="FIN">الشؤون المالية والخزينة (FIN)</option>
     <option value="FLT">الحركة والأسطول والورشة (FLT)</option>
     <option value="OPS">تشغيل المنصات (OPS)</option>
    </select>
   </label>
   <label>السجل التجاري الدائن / جهة الصرف<select name="creditorSponsorId">${options(spOptions)}</select></label>
   <label>الصندوق أو البنك الدافع للتكلفة<select name="paymentVaultCode">${options(vaultOptions)}</select></label>
   ${fieldInput('إجمالي مبلغ الذمة على المندوب (أو التكلفة)','totalAmount','','number',true)}
   <div id="company-share-wrap" class="hide">${fieldInput('مبلغ تحمل الشركة (يُقيد في حساب المصروف)','companyShareAmount','0','number')}</div>
   ${fieldInput('القسط الشهري المستقطع من الراتب','monthlyInstallment','','number',true)}
   ${fieldInput('مرجع المستند / السند / قرار الصرف','sourceRef',`HR-${Date.now().toString().slice(-6)}`,'text',true)}
   ${fieldInput('بداية سريان الاستقطاع','effectiveFrom',month,'month',true)}
   <label class="check full"><input name="requireMutualApproval" type="checkbox">يتطلب تصديق واعتماد القسم المقابل (المالية ⇄ HR) قبل تفعيل القيد</label>
  `:`
   <label>الحالة<select name="status">${options([['active','نشط'],['paused','إيقاف مؤقت']],d.status)}</select></label>
   ${fieldInput('القسط الشهري','monthlyInstallment',d.monthlyInstallment||'','number',true)}
   ${userRole==='Admin'&&!d.creditorSponsorId?`<label>ربط السجل الدائن بعد المراجعة<select name="creditorSponsorId"><option value="">اترك غير مربوط</option>${options(spOptions)}</select></label>`:''}
  `}
  <label class="full">ملاحظات وبيان القيد<textarea name="notes">${esc(d.notes||'')}</textarea></label>
  <label class="check full"><input name="agreementReviewed" type="checkbox" required>راجعت اتفاق الاستقطاع ومستند الصرف والطرف المتحمل للتكلفة.</label>
  <button>حفظ وترحيل القيد المحاسبي</button>
 </form>`);

 $('#debt-form input[name=totalAmount]')?.setAttribute('step','.01');
 $('#debt-form input[name=companyShareAmount]')?.setAttribute('step','.01');
 $('#debt-form input[name=monthlyInstallment]')?.setAttribute('step','.01');

 const allocSelect=$('#debt-allocation-mode');
 if(allocSelect){
  allocSelect.onchange=()=>{
   const mode=allocSelect.value;
   $('#company-share-wrap')?.classList.toggle('hide',mode!=='SharedSplit');
   const instInput=$('#debt-form input[name=monthlyInstallment]');
   if(instInput&&mode==='CompanyFull'){instInput.value='1';instInput.required=false;}
  };
 }
 const catSelect=$('#debt-category-select');
 if(catSelect){
  catSelect.onchange=()=>{
   const found=(catalog.categories||[]).find((c:any)=>c.code===catSelect.value);
   const totalInput=$('#debt-form input[name=totalAmount]');
   const instInput=$('#debt-form input[name=monthlyInstallment]');
   if(found&&found.defaultReferenceCost>0&&totalInput&&!totalInput.value){
    totalInput.value=String(found.defaultReferenceCost);
    if(instInput&&!instInput.value)instInput.value=String(Math.min(found.defaultReferenceCost,1000));
   }
  };
 }

 $('#debt-form').onsubmit=submit(async(v,form)=>{
  await api(id?`/debts/${id}`:'/debts',{expectedRemainingBalance:d.remainingBalance,
   ...v,
   originSponsorId:v.creditorSponsorId,
   requireMutualApproval:form.elements.requireMutualApproval?.checked||false,
   agreementReviewed:form.elements.agreementReviewed.checked
  },id?'PUT':'POST');
  $('#modal').close();
  await show('debts');
  toast(id?'تم تحديث جدولة المديونية':'تم حفظ السجل وتوليد القيد المحاسبي المزدوج بنجاح');
 });
}

function debtCollectDialog(id:string){
 const d=cache.debts?.find((x:any)=>x.id===id);
 if(!d)return;
 const catalog=cache.financeCatalog||{vaults:[]};
 const vaultOptions=(catalog.vaults||[]).map((v:any)=>[v.code,`${v.code} — ${v.nameAr}`]);

 modal(`تحصيل وتسوية مديونية — ${d.riderName||d.iqama}`,`<form id="debt-collect-form" class="form-grid">
  <div class="notice full">
   البند: <strong>${esc(d.categoryAr||d.category)}</strong> · السجل الدائن: <strong>${esc(d.creditorSponsorNameAr||'غير مربوط — راجع الجدولة')}</strong> · الرصيد المتبقي حالياً: <strong class="number">${money(d.remainingBalance)} ر.س</strong>
   <br><small>يمكنك تحصيل كامل المبلغ أو جزء منه نقداً أو بنكياً على أي صندوق (حتى لو كان يتبع سجلاً تجارياً آخر، حيث يقوم النظام آلياً بتسوية جاري السجلات الشقيقة 1310)، كما يمكنك منح إعفاء إداري مشروط لجزء من المديونية.</small>
  </div>
  ${fieldInput('المبلغ المحصل فعلياً (كاش أو بنك)','collectedAmount',String(d.monthlyInstallment||d.remainingBalance),'number',true)}
  ${fieldInput('مبلغ الإعفاء / التنازل الإداري (ح/ 5202)','waivedAmount','0','number')}
  <label>الصندوق أو البنك المستلم للمبلغ<select name="targetVaultCode">${options(vaultOptions,d.paymentVaultCode||'غير مسجل')}</select></label>
  <label>القسم القائم بالتحصيل
   <select name="collectorDepartment">
    <option value="FIN">الشؤون المالية والخزينة (اعتماد فوري)</option>
    <option value="HR">الموارد البشرية HR (يرسل إشعار تصديق للمالية)</option>
   </select>
  </label>
  ${fieldInput('رقم سند القبض / مرجع التحويل','receiptRef',`RCV-${Date.now().toString().slice(-6)}`,'text',true)}
  ${fieldInput('سبب الإعفاء الإداري (إن وجد)','waiverReason','خصم تشجيعي مقابل السداد الفوري','text')}
  <label class="full">ملاحظات السند<textarea name="notes"></textarea></label>
  <button class="good">تأكيد التحصيل وترحيل القيد المحاسبي</button>
 </form>`);
 $('#debt-collect-form input[name=collectedAmount]')?.setAttribute('step','.01');
 $('#debt-collect-form input[name=waivedAmount]')?.setAttribute('step','.01');
 $('#debt-collect-form').onsubmit=submit(async v=>{
  const res=await api(`/debts/${id}/collect`,v);
  $('#modal').close();
  await show('debts');
  toast(`تم تحصيل ${money(res.settlement.collectedAmount)} ر.س وإعفاء ${money(res.settlement.waivedAmount)} ر.س بموجب القيد ${res.journal.journalNumber}`);
 });
}

function debtTransferSponsorDialog(id:string){
 const d=cache.debts?.find((x:any)=>x.id===id);
 if(!d)return;
 const sponsors=(cache.financeCatalog?.sponsors||[]).filter((s:any)=>s.id!==d.creditorSponsorId);
 modal(`شراء وترحيل ملكية المديونية بين السجلات`, `<form id="debt-transfer-form" class="form-grid">
  <div class="notice full">
   المندوب: <strong>${esc(d.riderName||d.iqama)}</strong> · الرصيد المتبقي للنقل: <strong class="number">${money(d.remainingBalance)} ر.س</strong>
   <br>السجل الدائن الحالي (البائع): <strong>${esc(d.creditorSponsorNameAr||'غير مربوط — راجع الجدولة')}</strong>
  </div>
  <label class="full">السجل التجاري الجديد (المشتري للمديونية)
   <select name="newCreditorSponsorId" required>${options(sponsors.map((s:any)=>[s.id,`${s.shortNameAr} — ${s.nameAr} (${s.unifiedNumber})`]))}</select>
  </label>
  <label class="full">طريقة التسوية بين السجلين
   <select name="settlementMode">
    <option value="intercompany">قيد مقاصة في حساب جاري السجلات الشقيقة (1310)</option>
    <option value="cash_purchase">شراء نقدي مباشر بين الصناديق الداخلية للسجلين (MAIN)</option>
   </select>
  </label>
  ${fieldInput('رقم مرجع قرار النقل / الشراء','transferRef',`TRN-${Date.now().toString().slice(-6)}`,'text',true)}
  <button>اعتماد نقل المديونية وتوليد قيد المقاصة</button>
 </form>`);
 $('#debt-transfer-form').onsubmit=submit(async v=>{
  const res=await api(`/debts/${id}/transfer-sponsor`,v);
  $('#modal').close();
  await show('debts');
  toast(`تم نقل ملكية المديونية وإثبات القيد ${res.journal.journalNumber}`);
 });
}

function bulkCollectDialog(){
 const vaultOptions=(cache.financeCatalog?.vaults||[]).map((v:any)=>[v.code,`${v.code} — ${v.nameAr}`]);
 modal('شيت التحصيل المجمع للمديونيات (Excel / CSV / إدخال مباشر)',`
  <div class="notice">
   يمكنك رفع ملف Excel أو CSV يحتوي على الأعمدة: <b>(رقم الإقامة، اسم المندوب، المبلغ المحصل، مبلغ الإعفاء الإداري، رمز الصندوق، رقم السند، ملاحظات)</b>، أو لصق البيانات مباشرة. يقوم النظام بتوزيع المبلغ المحصل والإعفاء على مديونيات المندوب النشطة من الأقدم للأحدث وإصدار القيود المحاسبية.
  </div>
  <div class="form-grid">
   <label>الصندوق الافتراضي للتحصيل<select id="bulk-default-vault">${options(vaultOptions,'MAIN.BW.SET')}</select></label>
   <label>رفع ملف التحصيل المجمع (.xlsx أو .csv)<input id="bulk-collect-file" type="file" accept=".xlsx,.csv"></label>
  </div>
  <div class="actions" style="margin:12px 0">
   <button type="button" class="secondary" id="bulk-preview-btn">معاينة ملف Excel/CSV</button>
   <a class="button secondary" href="/api/debts/bulk-collect/template">تنزيل قالب Excel الجاهز</a>
  </div>
  <div id="bulk-preview-area"></div>
 `);
 $('#bulk-preview-btn').onclick=async()=>{
  try{
   const file=$('#bulk-collect-file').files?.[0];
   if(!file)throw Error('اختر ملف Excel أو CSV أولاً');
   const dataBase64=await new Promise<string>((resolve,reject)=>{const r=new FileReader();r.onload=()=>resolve(String(r.result).split(',')[1]);r.onerror=reject;r.readAsDataURL(file);});
   const res=await api('/debts/bulk-collect/preview-file',{filename:file.name,dataBase64,defaultVaultCode:$('#bulk-default-vault').value});
   $('#bulk-preview-area').innerHTML=`
    <div class="notice ${res.errorCount?'error':'good'}">صفوف صالحة: <b>${res.validCount}</b> · أخطاء: <b>${res.errorCount}</b></div>
    ${res.errors?.length?`<div class="pre">${esc(res.errors.join('\n'))}</div>`:''}
    ${res.rows?.length?table(['الصف','الإقامة','الاسم','مديونية حالية','المبلغ المحصل','الإعفاء الإداري','الصندوق','رقم السند'],res.rows.map((r:any)=>`<tr>
     <td>${r.row}</td><td class="number">${esc(r.iqama)}</td><td>${esc(r.riderName)}</td><td class="number">${money(r.riderRemainingBefore)}</td>
     <td class="number money-positive">${money(r.collectedAmount)}</td><td class="number">${money(r.waivedAmount)}</td><td class="number">${esc(r.targetVaultCode)}</td><td>${esc(r.receiptRef)}</td>
    </tr>`)):''}
    <button id="bulk-commit-btn" class="good" ${res.errorCount||!res.validCount?'disabled':''}>اعتماد وترحيل شيت التحصيل المجمع (${res.validCount} مندوب)</button>
   `;
   const commitBtn=$('#bulk-commit-btn');
   if(commitBtn){
    commitBtn.onclick=async()=>{
     try{
      commitBtn.disabled=true;
      const done=await api('/debts/bulk-collect/commit',{defaultVaultCode:$('#bulk-default-vault').value,rows:res.rows,fingerprint:res.fingerprint,confirm:true});
      $('#modal').close();
      await show('debts');
      toast(`تم ترحيل شيت التحصيل المجمع: محصل ${money(done.totalCollectedBatch)} ر.س وإعفاء ${money(done.totalWaivedBatch)} ر.س`);
     }catch(e:any){toast(e.message,true);commitBtn.disabled=false;}
    };
   }
  }catch(e:any){toast(e.message,true);}
 };
}

function vaultTransferDialog(){
 const vaultOptions=(cache.financeCatalog?.vaults||[]).map((v:any)=>[v.code,`${v.code} — ${v.nameAr}`]);
 modal('تحويل مالي بين الصناديق الداخلية وخزنة جاهز والبنوك',`<form id="vault-transfer-form" class="form-grid">
  <label>من صندوق / خزينة / بنك (طرف دائن ينقص)<select name="fromVaultCode" required>${options(vaultOptions,'SUP.BW.SET')}</select></label>
  <label>إلى صندوق / خزينة / بنك (طرف مدين يزيد)<select name="toVaultCode" required>${options(vaultOptions,'MAIN.BW.SET')}</select></label>
  ${fieldInput('المبلغ المحول (ر.س)','amount','','number',true)}
  ${fieldInput('رقم مرجع التحويل / الإيداع','sourceRef',`VTR-${Date.now().toString().slice(-6)}`,'text',true)}
  <label class="full">البيان والتفاصيل<input name="description" value="توريد نقدية من خزنة جاهز إلى الصندوق الداخلي للشركة"></label>
  <div class="notice full">إذا كان التحويل بين صندوقين يتبعان سجلين تجاريين مختلفين، يُولّد النظام تلقائياً سطري مقاصة في حساب <b>جاري السجلات الشقيقة (1310)</b>.</div>
  <button>تنفيذ التحويل وتوليد القيد المحاسبي</button>
 </form>`);
 $('#vault-transfer-form input[name=amount]')?.setAttribute('step','.01');
 $('#vault-transfer-form').onsubmit=submit(async v=>{
  const j=await api('/finance/vault-transfer',v);
  $('#modal').close();
  await show('debts');
  toast(`تم ترحيل قيد التحويل رقم ${j.journalNumber}`);
 });
}

function journalDetailDialog(id:string){
 const j=(cache.journals||[]).find((x:any)=>x.id===id);
 if(!j)return;
 modal(`تفاصيل القيد المحاسبي المزدوج — ${j.journalNumber}`,`
  <div class="notice">
   <strong>${esc(j.description)}</strong><br>
   التاريخ: <span class="number">${esc(j.date)}</span> · المرجع: <span class="number">${esc(j.sourceRef)}</span> · السجل المنشئ: <b>${esc(j.originSponsorNameAr)}</b> · السجل الدائن: <b>${esc(j.creditorSponsorNameAr)}</b>
  </div>
  ${table(
   ['#','رمز الحساب','اسم الحساب','السجل التجاري','طبيعة الحساب','مدين (Debit)','دائن (Credit)','الأثر المحاسبي على الرصيد'],
   (j.lines||[]).map((l:any)=>`<tr>
    <td>${l.lineNo}</td>
    <td class="number"><strong>${esc(l.accountCode)}</strong></td>
    <td>${esc(l.accountNameAr)}</td>
    <td>${esc(l.sponsorNameAr)}</td>
    <td>${l.naturalSide==='Debit'?'مدين':'دائن'}</td>
    <td class="number ${l.debit>0?'money-positive':''}">${l.debit>0?money(l.debit):'—'}</td>
    <td class="number ${l.credit>0?'money-negative':''}">${l.credit>0?money(l.credit):'—'}</td>
    <td class="small">${badge(l.effectDirection==='increase'?'+ زيادة رصيد':'- نقص رصيد',l.effectDirection==='increase'?'good':'warn')} ${esc(l.effectExplanationAr)}</td>
   </tr>`)
  )}
  <div class="notice good">إجمالي المدين: <b>${money(j.totalDebit)} ر.س</b> = إجمالي الدائن: <b>${money(j.totalCredit)} ر.س</b> (قيد متوازن 100%)</div>
  <button class="secondary no-print" data-action="print">طباعة سند القيد المحاسبي</button>
 `);
}

function printVoucherDialog(debtId?:string){
 const d=debtId?(cache.debts||[]).find((x:any)=>x.id===debtId):(cache.debts||[])[0];
 const rName=d?.riderName||cache.riders?.find((r:any)=>r.iqama===d?.iqama)?.name||'________________________';
 const iqama=d?.iqama||'________________';
 const spName=d?.creditorSponsorNameAr||'شركة البوابة المقبلة للتجارة';
 const amount=d?money(d.totalAmount):'0.00';
 const inst=d?money(d.monthlyInstallment):'0.00';
 const catAr=d?.categoryAr||d?.category||'سلفة نقدية / خدمة موارد بشرية';
 modal('نموذج إقرار سلفة نقدية / بنكية وسند استقطاع رسمي (متوافق مع نظام البوابة)',`
  <div class="panel" style="border:2px solid var(--ink);padding:24px">
   <div style="display:flex;justify-content:space-between;align-items:center;border-bottom:2px solid var(--line);padding-bottom:12px;margin-bottom:16px">
    <div>
     <h2 style="margin:0">${esc(spName)}</h2>
     <div class="muted small">إدارة الموارد البشرية والشؤون المالية — نموذج رقم: ${esc(d?.sourceRef||'ADV-FORM')}</div>
    </div>
    <div class="number" style="text-align:left">
     <strong>التاريخ: ${new Date().toISOString().slice(0,10)}</strong><br>
     <span class="small">رقم القيد: ${esc(d?.journalNumber||'—')}</span>
    </div>
   </div>
   <h3 style="text-align:center;margin:12px 0">إقرار استلام سلفة / تحمل تكلفة خدمة وجدولة استقطاع</h3>
   <div class="form-grid" style="margin:16px 0">
    <div><span class="muted small">اسم المندوب / الموظف:</span><br><strong>${esc(rName)}</strong></div>
    <div><span class="muted small">رقم الهوية / الإقامة:</span><br><strong class="number">${esc(iqama)}</strong></div>
    <div><span class="muted small">نوع البند / المعاملة:</span><br><strong>${esc(catAr)} (${esc(allocationLabels[d?.allocationMode||'RiderFull'])})</strong></div>
    <div><span class="muted small">الصندوق / الحساب الصارف:</span><br><strong class="number">${esc(d?.paymentVaultCode||'MAIN.BW.SET')}</strong></div>
    <div><span class="muted small">مبلغ الذمة على المندوب:</span><br><strong class="number">${amount} ريال سعودي</strong></div>
    <div><span class="muted small">القسط الشهري المتفق عليه:</span><br><strong class="number">${inst} ريال سعودي</strong></div>
   </div>
   <p style="line-height:2;font-size:13px">
    أقر أنا الموضح اسمي وهويتي أعلاه بأنني استلمت / استفدت من البند المذكور أعلاه والمقيد في ذمتي لصالح (<b>${esc(spName)}</b>)، وأفوض الشركة تفويضاً نهائياً لا رجعة فيه باستقطاع القسط الشهري المحدد أعلاه من مستحقاتي وعمولاتي الشهرية، وفي حال انتهاء العلاقة التعاقدية أو التوقف عن العمل يحق للشركة خصم كامل الرصيد المتبقي من أي مستحقات أو مطالبتي بسداده فوراً لصندوق الشركة الداخلي.
   </p>
   <div style="display:grid;grid-template-columns:repeat(3,1fr);gap:16px;margin-top:28px;padding-top:16px;border-top:1px dashed var(--line);text-align:center">
    <div><strong>توقيع وبصمة المستلم (المدين)</strong><br><br>______________________</div>
    <div><strong>اعتماد الموارد البشرية (HR)</strong><br><br>______________________</div>
    <div><strong>اعتماد الشؤون المالية والخزينة</strong><br><br>______________________</div>
   </div>
  </div>
  <div class="actions no-print" style="margin-top:14px">
   <button data-action="print">🖨️ طباعة النموذج الرسمي</button>
  </div>
 `);
}

function approvalDecideDialog(id:string){
 const a=(cache.approvals||[]).find((x:any)=>x.id===id);
 if(!a)return;
 modal(`قرار التصديق المتبادل — ${a.titleAr}`,`<form id="approval-decide-form" class="form-grid">
  <div class="notice full">${esc(a.bodyAr)}</div>
  <label class="full">القرار الإداري/المالي
   <select name="decision" required>
    <option value="approved">تصديق واعتماد وترحيل القيد (Approved)</option>
    <option value="suspended">تعليق لطلب استكمال نواقص (Suspended)</option>
    <option value="rejected">رفض وإلغاء الطلب (Rejected)</option>
   </select>
  </label>
  <label class="full">ملاحظة المراجعة أو سبب التعليق/الرفض<textarea name="note" placeholder="اكتب ملاحظة التصديق أو سبب الرفض/التعليق..."></textarea></label>
  <button class="good">حفظ القرار وإشعار القسم المرسل</button>
 </form>`);
 $('#approval-decide-form').onsubmit=submit(async v=>{
  await api(`/approvals/${id}/decide`,v);
  $('#modal').close();
  await show('debts');
  toast('تم حفظ قرار التصديق وتحديث حالة القيد والإشعارات');
 });
}

function notifNewDialog(){
 modal('إنشاء إشعار إداري أو مهمة (To-Do) لقسم',`<form id="notif-new-form" class="form-grid">
  <label>نوع الإشعار
   <select name="tab">
    <option value="todo">مهمة تنفيذية (To-Do List)</option>
    <option value="general">إشعار وتعميم إداري عام</option>
    <option value="approvals">تنبيه مراجعة ومطابقة</option>
   </select>
  </label>
  <label>القسم المستهدف
   <select name="targetDepartment">
    <option value="FIN">الشؤون المالية والخزينة (FIN)</option>
    <option value="HR">الموارد البشرية (HR)</option>
    <option value="FLT">الحركة والأسطول (FLT)</option>
    <option value="OPS">التشغيل والمنصات (OPS)</option>
    <option value="ALL">جميع الأقسام</option>
   </select>
  </label>
  <label>الرتبة الدنيا المطلوبة
   <select name="requiredRankLevel">
    <option value="1">رتبة 1 (أخصائي / محاسب صندوق)</option>
    <option value="2">رتبة 2 (مسؤول علاقات / محاسب عام)</option>
    <option value="3">رتبة 3 (رئيس حسابات / مدير قسم)</option>
    <option value="4">رتبة 4 (مدير مالي / إدارة عليا)</option>
   </select>
  </label>
  ${fieldInput('عنوان الإشعار أو المهمة','titleAr','','text',true)}
  <label class="full">تفاصيل المهمة أو التوجيه<textarea name="bodyAr" required></textarea></label>
  <button>إرسال الإشعار / المهمة</button>
 </form>`);
 $('#notif-new-form').onsubmit=submit(async v=>{
  await api('/notifications/center',{...v,requiredRankLevel:Number(v.requiredRankLevel)});
  $('#modal').close();
  await show('debts');
  toast('تم إرسال الإشعار وإضافته لمركز المهام');
 });
}

function mobileBroadcastDialog(defaultIqama='ALL'){
 modal('إرسال إشعار لتطبيق المندوب على Google Play (Firebase FCM)',`<form id="mobile-broadcast-form" class="form-grid">
  <label>المستهدفون بالإشعار
   <select name="iqama">
    <option value="ALL" ${defaultIqama==='ALL'?'selected':''}>جميع المناديب النشطين على التطبيق (Broadcast)</option>
    ${options((cache.riders||[]).map((r:any)=>[r.iqama,`${r.name} — ${r.iqama}`]),defaultIqama)}
   </select>
  </label>
  <label>تصنيف الإشعار
   <select name="category">
    <option value="financial">كشف حساب / إشعار مديونية أو راتب</option>
    <option value="form_link">مشاركة رابط نموذج (Form / استبيان)</option>
    <option value="operational">توجيه تشغيلي ميداني</option>
    <option value="administrative">تعليمات نظامية وإدارية</option>
   </select>
  </label>
  ${fieldInput('عنوان الإشعار على شاشة جوال المندوب','titleAr',defaultIqama!=='ALL'?'إشعار بتحديث كشف حسابك ومديونيتك':'تعميم تشغيلي من إدارة البوابة','text',true)}
  ${fieldInput('رابط الفورم أو الكشف المرفق (اختياري)','actionUrl',location.origin+'/inquiry','text')}
  <label class="full">نص الرسالة<textarea name="bodyAr" required>${defaultIqama!=='ALL'?'يرجى فتح التطبيق لمراجعة كشف الحساب وتفاصيل الأقساط المحدثة.':'يرجى الالتزام بالتعليمات التشغيلية ومراجعة النماذج المرفقة.'}</textarea></label>
  <label class="check full"><input type="checkbox" name="requiresAck" checked>يتطلب ضغط المندوب على زر «قرأت وأوافق» داخل التطبيق</label>
  <button>حفظ الإشعار في بوابة المندوب</button>
 </form>`);
 $('#mobile-broadcast-form').onsubmit=submit(async(v,form)=>{
  await api('/rider-mobile/broadcast',{...v,requiresAck:form.elements.requiresAck.checked});
  $('#modal').close();
  await show('debts');
  toast('تم حفظ الإشعار في بوابة المندوب');
 });
}

function openingDebtImportDialog(){
 const cats=cache.financeCatalog?.categories||[];
 modal('رفع رصيد أول المدة وشيت استيراد المديونيات مع بيان مالي تفصيلي قبل التحميل',`
  <div class="notice">
   <strong>كيف يعمل شيت رفع أرصدة أول المدة للمديونيات؟</strong><br>
   1. نزّل القالب الجاهز بصيغة Excel أدناه (يحتوي على القوائم المنسدلة لجميع الـ 22 بنداً والسجلات التجارية الثلاثة BW / EX / TR والحساب المقابل 3101).<br>
   2. ارفع الملف واضغط <b>«معاينة وإصدار البيان المالي قبل التحميل»</b> لعرض ملخص السجلات التجارية، وملخص البنود والحسابات (الذمم المدينة 1210/1220 ومصروفات HR 5101..5301 وحساب أرصدة أول المدة 3101)، وتفاصيل كل صف قبل اعتماده.
  </div>
  <details style="margin:10px 0;background:var(--raised);padding:10px 14px;border-radius:10px">
   <summary style="cursor:pointer;font-weight:700">📋 عرض دليل البنود والتصنيفات والحسابات المحاسبية المعتمدة في الشيت (${cats.length} بند)</summary>
   <div style="margin-top:10px">
    ${table(
     ['كود البند (Category)','اسم البند بالعربية','القسم','حساب الذمم المدينة','حساب المصروف المرتبط','التكلفة المرجعية'],
     cats.map((c:any)=>`<tr>
      <td class="number"><strong>${esc(c.code)}</strong></td>
      <td>${esc(c.nameAr)}</td>
      <td>${esc(c.sector)}</td>
      <td class="number">ح/ ${esc(c.receivableAccountCode)}</td>
      <td class="number">ح/ ${esc(c.expenseAccountCode)}</td>
      <td class="number">${money(c.defaultReferenceCost)} ر.س</td>
     </tr>`)
    )}
   </div>
  </details>
  <div class="form-grid" style="margin-top:12px">
   <label class="full">اختر ملف شيت أرصدة أول المدة / المديونيات (.xlsx أو .csv)
    <input id="opening-debt-file" type="file" accept=".xlsx,.csv">
   </label>
  </div>
  <div class="actions" style="margin:12px 0">
   <button type="button" class="good" id="opening-preview-btn">🔍 معاينة وإصدار البيان المالي قبل التحميل</button>
   <a class="button secondary" href="/api/debts/import-opening/template">📥 تنزيل قالب Excel لأرصدة أول المدة والمديونيات</a>
  </div>
  <div id="opening-preview-area"></div>
 `);

 $('#opening-preview-btn').onclick=async()=>{
  try{
   const file=$('#opening-debt-file').files?.[0];
   if(!file)throw Error('اختر ملف Excel أو CSV أولاً');
   const dataBase64=await new Promise<string>((resolve,reject)=>{const r=new FileReader();r.onload=()=>resolve(String(r.result).split(',')[1]);r.onerror=reject;r.readAsDataURL(file);});
   const res=await api('/debts/import-opening/preview',{filename:file.name,dataBase64});
   $('#opening-preview-area').innerHTML=`
    <hr>
    <h3>📊 البيان المالي والمحاسبي التفصيلي قبل التحميل والاعتماد</h3>
    <div class="stats" style="margin-bottom:12px">
     <div class="stat"><div class="label">صفوف صالحة / أخطاء</div><strong class="number ${res.errorCount?'money-negative':'money-positive'}">${res.validCount} صالح · ${res.errorCount} خطأ</strong><div class="hint">${res.newRidersToCreateCount} مندوب جديد سيُنشأ تلقائياً</div></div>
     <div class="stat"><div class="label">إجمالي المديونيات الأصلية</div><strong class="number">${money(res.totalOriginalDebt)} ر.س</strong><div class="hint">المسدد سابقاً: ${money(res.totalPriorPaid)} ر.س</div></div>
     <div class="stat"><div class="label">صافي رصيد أول المدة المتبقي</div><strong class="number money-positive">${money(res.totalOpeningRemaining)} ر.س</strong><div class="hint">يُقيد في الذمم المدينة (1210/1220)</div></div>
     <div class="stat"><div class="label">حصة تحمل الشركة (المصروفات)</div><strong class="number">${money(res.totalCompanyExpenseShare)} ر.س</strong><div class="hint">توازن القيد: مدين ${money(res.totalDebits)} = دائن ${money(res.totalCredits)}</div></div>
    </div>
    ${res.errors?.length?`<div class="notice error"><strong>أخطاء تحتاج تصحيحاً قبل التحميل:</strong><div class="pre">${esc(res.errors.join('\n'))}</div></div>`:''}
    <h4>1. بيان توزيع أرصدة أول المدة حسب السجل التجاري الدائن</h4>
    ${table(
     ['السجل التجاري الدائن','الرقم الموحد','عدد السجلات','عدد المناديب','إجمالي المديونية الأصلية','المسدد سابقاً','صافي رصيد أول المدة المتبقي','حصة تحمل الشركة'],
     (res.summaryBySponsor||[]).map((s:any)=>`<tr>
      <td><strong>${esc(s.shortNameAr)}</strong> (${esc(s.shortCode)})</td>
      <td class="number">${esc(s.unifiedNumber)}</td>
      <td class="number">${s.rowsCount}</td>
      <td class="number">${s.uniqueRidersCount}</td>
      <td class="number">${money(s.totalOriginalDebt)}</td>
      <td class="number">${money(s.totalPriorPaid)}</td>
      <td class="number money-positive"><strong>${money(s.totalOpeningRemaining)} ر.س</strong></td>
      <td class="number">${money(s.totalCompanyShare)} ر.س</td>
     </tr>`)
    )}
    <h4 style="margin-top:14px">2. بيان توزيع الأرصدة حسب تصنيف البند والحساب المحاسبي</h4>
    ${table(
     ['كود البند','اسم البند','القسم','حساب الذمم المدينة','حساب المصروف','عدد السجلات','صافي الذمة المدينة على المناديب','حصة المصروف على الشركة'],
     (res.summaryByCategory||[]).map((c:any)=>`<tr>
      <td class="number"><strong>${esc(c.categoryCode)}</strong></td>
      <td>${esc(c.categoryAr)}</td>
      <td>${esc(c.sector)}</td>
      <td class="number">${esc(c.receivableAccountCode)} — ${esc(c.receivableAccountNameAr)}</td>
      <td class="number">${esc(c.expenseAccountCode)} — ${esc(c.expenseAccountNameAr)}</td>
      <td class="number">${c.rowsCount}</td>
      <td class="number money-positive"><strong>${money(c.totalOpeningRemaining)} ر.س</strong></td>
      <td class="number">${money(c.totalCompanyShare)} ر.س</td>
     </tr>`)
    )}
    <h4 style="margin-top:14px">3. البيان التفصيلي للصفوف والقيود المحاسبية المقابلة (${(res.rows||[]).length} صف)</h4>
    ${table(
     ['#','الإقامة','الاسم','البند','السجل الدائن','نوع القيد','الإجمالي','المسدد سابقاً','الصافي المتبقي','حصة الشركة','القسط الشهري','حساب المدين','حساب الدائن المقابل','المرجع'],
     (res.rows||[]).map((r:any)=>`<tr>
      <td>${r.row}</td>
      <td class="number">${esc(r.iqama)}</td>
      <td>${esc(r.riderName)} ${!r.riderExists?badge('جديد','warn'):''}</td>
      <td>${esc(r.categoryAr)}</td>
      <td>${esc(r.creditorSponsorNameAr)}</td>
      <td>${esc(r.entryModeAr)}</td>
      <td class="number">${money(r.totalAmount)}</td>
      <td class="number">${money(r.priorPaidAmount)}</td>
      <td class="number money-positive"><strong>${money(r.remainingBalance)}</strong></td>
      <td class="number">${money(r.companyShareAmount)}</td>
      <td class="number">${money(r.monthlyInstallment)}</td>
      <td class="small number">${esc(r.receivableAccountCode)}${r.companyShareAmount>0?' + '+esc(r.expenseAccountCode):''}</td>
      <td class="small number">${esc(r.contraAccountCode)} (${esc(r.contraAccountNameAr)})</td>
      <td class="number">${esc(r.sourceRef)}</td>
     </tr>`)
    )}
    <div class="actions" style="margin-top:14px">
     <button id="opening-commit-btn" class="good" ${res.errorCount||!res.validCount?'disabled':''}>✓ اعتماد البيان وتحميل أرصدة أول المدة وتوليد القيود المحاسبية (${res.validCount} سجل)</button>
    </div>
   `;
   const commitBtn=$('#opening-commit-btn');
   if(commitBtn){
    commitBtn.onclick=async()=>{
     try{
      commitBtn.disabled=true;
      const done=await api('/debts/import-opening/commit',{rows:res.rows,fingerprint:res.fingerprint,confirm:true});
      $('#modal').close();
      await show('debts');
      toast(`تم تحميل واعتماد ${done.importedCount} مديونية بصافي رصيد ${money(done.totalRemainingImported)} ر.س`);
     }catch(e:any){toast(e.message,true);commitBtn.disabled=false;}
    };
   }
  }catch(e:any){toast(e.message,true);}
 };
}

async function firebaseConfigDialog(){
 const cfg=await api('/rider-mobile/firebase-config');
 modal('إعدادات ربط Firebase Cloud Messaging (FCM) وتطبيق المندوب على Google Play',`
  <div class="stats" style="margin-bottom:14px">
   <div class="stat"><div class="label">حالة ربط Firebase FCM</div><strong>${cfg.configured?'إعدادات محفوظة؛ الإرسال الخارجي غير مفعّل':'غير مهيأ'}</strong><div class="hint">Project: ${esc(cfg.projectId)}</div></div>
   <div class="stat"><div class="label">الأجهزة المسجلة بـ FCM Token</div><strong class="number">${cfg.fcmRegisteredDevicesCount} / ${cfg.activeSessionsCount}</strong><div class="hint">جلسات تطبيق المناديب النشطة</div></div>
   <div class="stat"><div class="label">رابط تطبيق المندوب المباشر</div><strong dir="ltr" class="small">${esc(cfg.webMobilePortalUrl)}</strong><div class="hint"><a href="/rider-app" target="_blank">فتح ومعاينة التطبيق الآن ↗</a></div></div>
  </div>
  <div class="notice">
   <strong>كيف يعمل الربط مع تطبيق Google Play وخدمات Firebase؟</strong><br>
   1. <b>رابط التطبيق الحالي الجاهز للاختبار الفوري:</b> <a href="/rider-app" target="_blank" dir="ltr"><b>${esc(cfg.webMobilePortalUrl)}</b></a> (يدعم تسجيل دخول المندوب برقم الإقامة، عرض كشف الحساب والمديونيات، واستقبال الإشعارات والضغط على «قرأت وأوافق»).<br>
   2. <b>لربط إشعارات الدفع الحقيقية (Push Notifications) على أجهزة أندرويد:</b> أنشئ مشروعاً في <span dir="ltr">Firebase Console</span>، وأدخل <code>Project ID</code> و <code>Sender ID</code> ومحتوى ملف <code>service-account.json</code> أدناه.
  </div>
  <form id="firebase-cfg-form" class="form-grid" style="margin-top:12px">
   ${fieldInput('معرف مشروع فايربيز (Firebase Project ID)','projectId',cfg.projectId,'text',true)}
   ${fieldInput('رقم المرسل (FCM Sender ID)','senderId',cfg.senderId||'','text')}
   ${fieldInput('اسم حزمة تطبيق Google Play (Package Name)','androidPackageName',cfg.androidPackageName||'com.albawaba.gate.rider','text',true)}
   ${fieldInput('رابط التطبيق على متجر Google Play','googlePlayUrl',cfg.googlePlayUrl||'','text')}
   <label class="full">مفتاح Web Push العام (VAPID Public Key — اختياري)<input name="vapidPublicKey" dir="ltr" value="${esc(cfg.vapidPublicKey||'')}"></label>
   <label class="full">محتوى ملف حساب الخدمة (Firebase Service Account JSON — اختياري للإرسال المباشر عبر FCM HTTP v1)<textarea name="serviceAccountJson" dir="ltr" placeholder='{"type":"service_account","project_id":"..."}'></textarea></label>
   <button class="good">حفظ إعدادات Firebase وتطبيق Google Play</button>
  </form>
 `);
 $('#firebase-cfg-form').onsubmit=submit(async v=>{
  await api('/rider-mobile/firebase-config',v);
  $('#modal').close();
  toast('تم حفظ إعدادات ربط Firebase وتطبيق Google Play بنجاح');
 });
}

function promissoryNewDialog(){
 const spOptions=(cache.financeCatalog?.sponsors||[]).map((s:any)=>[s.id,`${s.shortNameAr} — ${s.nameAr}`]);
 modal('أرشفة سند لأمر جديد (منصة نافذ)',`<form id="promissory-form" class="form-grid">
  <label>المندوب / الموظف المدين<select name="iqama" required>${riderOptions()}</select></label>
  <label>القسم المختص<select name="department"><option value="HR">الموارد البشرية (HR)</option><option value="Fleet">الحركة والأسطول (Fleet)</option><option value="Platforms">المنصات (Platforms)</option></select></label>
  <label class="full">السجل التجاري المستفيد في نافذ<select name="beneficiarySponsorId">${options(spOptions)}</select></label>
  ${fieldInput('رقم سند الأمر في نافذ','noteNumber',`NFZ-${Date.now().toString().slice(-6)}`,'text',true)}
  ${fieldInput('قيمة السند (ر.س)','amount','15000','number',true)}
  <label class="full">ملاحظات السند<textarea name="notes"></textarea></label>
  <button>حفظ في الأرشيف الموحد</button>
 </form>`);
 $('#promissory-form').onsubmit=submit(async v=>{
  const r=(cache.riders||[]).find((x:any)=>x.iqama===v.iqama);
  await api('/promissory-notes',{...v,debtorName:r?.name||v.iqama});
  $('#modal').close();
  await show('debts');
  toast('تمت أرشفة سند الأمر بنجاح');
 });
}

async function baselineDialog(){
 modal('مزامنة الدليل المرجعي واسترجاع بيانات المناديب (826 مندوب)',`
  <div class="notice">
   يمكنك المزامنة مباشرة من <strong>ملف الدليل المرجعي المدمج في الخادم (826 مندوب مطهر وموحد من 6 ملفات رسمية)</strong> بضغطة زر، أو رفع ملف <code>riders-baseline.json</code> خارجي. يُستبعد الإداريون (36 موظفاً) تلقائياً، وتُستكمل الحقول الفارغة للمناديب الحاليين دون استبدال بياناتهم المعتمدة.
  </div>
  <div class="actions" style="margin:12px 0">
   <button type="button" class="good" id="baseline-local-preview">⚡ معاينة ومزامنة الدليل المرجعي المدمج (826 مندوب)</button>
  </div>
  <hr>
  <p class="muted small">أو ارفع ملف JSON خارجي من جهازك:</p>
  <input id="baseline-file" type="file" accept=".json">
  <button id="baseline-preview" class="secondary">معاينة الملف المرفوع</button>
  <div id="baseline-result" style="margin-top:12px"></div>
 `);
 const renderPreview=(result:any,payload:any)=>{
  $('#baseline-result').innerHTML=`<div class="notice">${result.total} مندوب · ${result.excludedAdministrative} إداري مستبعد · ${result.addCount} إضافة جديدة · ${result.updateCount||0} استكمال حقول مفقودة · ${result.conflicts?.length||0} تعارض محفوظ دون استبدال · ${result.existingCount} موجود مسبقاً · ${result.errors.length} خطأ</div>${result.errors.map((x:any)=>`<p>الصف ${x.row}: ${esc(x.error)}</p>`).join('')}<button id="baseline-commit" class="good" ${result.errors.length?'disabled':''}>تأكيد الاستيراد واستكمال البيانات</button>`;
  $('#baseline-commit').onclick=async()=>{
   try{
    const done=await api('/riders/sync-baseline',{...payload,mode:'commit',fingerprint:result.fingerprint});
    $('#modal').close();
    await show('riders');
    toast(`تمت مزامنة الدليل بنجاح: أُضيف ${done.count||0} مندوب واستُكمل ${done.updatedCount||0} سجل`);
   }catch(e:any){toast(e.message,true);}
  };
 };
 $('#baseline-local-preview').onclick=async()=>{
  try{
   const result=await api('/riders/sync-baseline',{useLocalBaseline:true,mode:'preview'});
   renderPreview(result,{useLocalBaseline:true});
  }catch(e:any){toast(e.message,true);}
 };
 $('#baseline-preview').onclick=async()=>{
  try{
   const file=$('#baseline-file').files[0];
   if(!file)throw Error('اختر الملف');
   const riders=JSON.parse(await file.text());
   const result=await api('/riders/sync-baseline',{riders,mode:'preview'});
   renderPreview(result,{riders});
  }catch(e:any){toast(e.message,true);}
 };
}

async function riderProfile360Dialog(iqama:string){
 const p=await api(`/riders/${encodeURIComponent(iqama)}/profile-360`);
 const r=p.rider||{};
 const checklist=p.documentChecklist||[];
 const uploadedCount=checklist.filter((d:any)=>d.isUploaded).length;
 modal(`الملف الشامل والوثائق الرسمية للمندوب 360° — ${r.name||iqama}`,`
  <div class="stats" style="margin-bottom:14px">
   <div class="stat"><div class="label">رقم الإقامة / الهوية</div><strong class="number">${esc(r.iqama)}</strong><div class="hint">${esc(r.nationality||'—')} · ${esc(r.city||'—')}</div></div>
   <div class="stat"><div class="label">الكفيل / السجل التجاري</div><strong>${esc(r.sponsor||'غير محدد')}</strong><div class="hint number">${esc(r.sponsorId||'—')}</div></div>
   <div class="stat"><div class="label">اكتمال الملف الوثائقي</div><strong class="number ${uploadedCount===checklist.length?'money-positive':' warn'}">${uploadedCount} / ${checklist.length} وثائق</strong><div class="hint">مطابق لهيكل وثائق نظام البوابة</div></div>
   <div class="stat"><div class="label">المديونيات والطلبات</div><strong class="number ${p.totalRemainingDebt>0?'money-negative':'money-positive'}">${money(p.totalRemainingDebt)} ر.س</strong><div class="hint">${p.activeDebtsCount} مديونية نشطة · ${p.totalOrders} طلب مسجل</div></div>
  </div>
  <div class="panel" style="margin-bottom:14px">
   <div class="form-grid">
    <div><span class="muted small">الجوال الدولي:</span><br><strong dir="ltr" class="number">${esc(r.phone||'—')}</strong></div>
    <div><span class="muted small">نوع العلاقة والمركبة:</span><br><strong>${esc({SponsoredInternal:'كفالة الشركة',External:'خارجي'}[r.engagementType]||'—')} · ${esc({Car:'سيارة',Motorcycle:'دباب'}[r.operationalRole]||'—')}</strong></div>
    <div><span class="muted small">رخصة القيادة والمنصات:</span><br><strong>${esc(r.drivingLicense||'—')} · ${esc(r.platforms||'—')}</strong></div>
    <div><span class="muted small">الآيبان البنكي (IBAN):</span><br><strong dir="ltr" class="number">${esc(r.iban||'غير متاح لدورك أو غير مسجل')}</strong></div>
   </div>
  </div>
  <h3>الأرشيف الوثائقي الرسمي للمندوب (7 مستندات أساسية في نظام البوابة ERP)</h3>
  ${table(
   ['المستند الرسمي','المعرف القياسي (GUID)','الحالة','رقم الوثيقة','تاريخ الانتهاء','مسار الحفظ المهيأ للدمج'],
   checklist.map((d:any)=>`<tr>
    <td><strong>${esc(d.nameAr)}</strong><div class="muted small number">${esc(d.code)}</div></td>
    <td class="small number muted">${esc(d.documentTypeId)}</td>
    <td>${badge(d.isUploaded?'مرفوع ومؤرشف':'ناقص (Missing)',d.isUploaded?'good':'warn')}</td>
    <td class="number">${esc(d.documentNumber||'—')}</td>
    <td class="number">${esc(d.expiryDate||'—')}</td>
    <td dir="ltr" class="small muted">${esc(d.storagePath)}</td>
   </tr>`)
  )}
  ${userRole==='Admin'?`
   <hr>
   <h3>إرفاق / تحديث وثيقة رسمية للمندوب</h3>
   <form id="rider-doc-form" class="form-grid">
    <label>نوع المستند الرسمي
     <select name="documentTypeCode" required>
      ${options(checklist.map((d:any)=>[d.code,`${d.nameAr} (${d.code})`]))}
     </select>
    </label>
    ${fieldInput('رقم الوثيقة / الرخصة / العقد','documentNumber',r.iqama,'text',true)}
    ${fieldInput('تاريخ الإصدار','issueDate',new Date().toISOString().slice(0,10),'date')}
    ${fieldInput('تاريخ الانتهاء','expiryDate',new Date(Date.now()+365*864e5).toISOString().slice(0,10),'date')}
    <label class="full">ملف الوثيقة المرفق (PDF أو صورة — اختياري)<input id="rider-doc-file" type="file" accept=".pdf,.png,.jpg,.jpeg"></label>
    <label class="full">ملاحظات الأرشفة<input name="notes" placeholder="مثال: تم التحقق من سريان الوثيقة في مقيم / أبشر / نافذ"></label>
    <button class="good">حفظ وأرشفة الوثيقة في ملف المندوب</button>
   </form>
  `:''}
 `);
 const docForm=$('#rider-doc-form');
 if(docForm){
  docForm.onsubmit=submit(async v=>{
   let dataBase64='',fileName='',contentType='application/pdf';
   const file=$('#rider-doc-file')?.files?.[0];
   if(file){
    fileName=file.name;contentType=file.type||'application/octet-stream';
    dataBase64=await new Promise<string>((resolve,reject)=>{const fr=new FileReader();fr.onload=()=>resolve(String(fr.result).split(',')[1]);fr.onerror=reject;fr.readAsDataURL(file);});
   }
   await api(`/riders/${encodeURIComponent(iqama)}/documents`,{...v,fileName:fileName||undefined,contentType,dataBase64});
   toast('تم حفظ الوثيقة في أرشيف المندوب بنجاح');
   await riderProfile360Dialog(iqama);
  });
 }
}
function questionCard(f:any={},index=0){return `<fieldset class="builder-question"><legend>سؤال ${index+1}</legend><div class="form-grid">${fieldInput('معرف إنجليزي فريد','id',f.id||'q'+(index+1),'text',true)}<label>النوع<select name="type">${options(['text','number','select','radio','iqama','phone','date'].map(x=>[x,x]),f.type||'text')}</select></label>${fieldInput('السؤال بالعربية','labelAr',f.labelAr||'','text',true)}${fieldInput('English label','labelEn',f.labelEn||'')}${fieldInput('اردو سوال','labelUr',f.labelUr||'')}<label>الخيارات: قيمة | عربي | English | اردو، سطر لكل خيار<textarea name="options">${esc((f.options||[]).map(x=>typeof x==='string'?x:[x.value,x.labelAr||x.value,x.labelEn||x.value,x.labelUr||x.value].join(' | ')).join('\n'))}</textarea></label><label class="check"><input type="checkbox" name="required" ${f.required?'checked':''}>إجابة مطلوبة</label><button type="button" class="ghost" data-remove-question>حذف السؤال</button></div></fieldset>`;}
function builderDialog(id?:string){const f=cache.customForms?.find(x=>x.id===id)||{};modal('مصمم النموذج',`<form id="builder-form"> <div class="form-grid">${fieldInput('العنوان بالعربية','titleAr',f.titleAr||'','text',true)}${fieldInput('English title','titleEn',f.titleEn||'')}${fieldInput('اردو عنوان','titleUr',f.titleUr||'')}${fieldInput('انتهاء النموذج','expiresAt',(f.expiresAt||new Date(Date.now()+14*864e5).toISOString()).slice(0,16),'datetime-local',true)}${fieldInput('الحد الأقصى للردود','maxSubmissions',String(f.maxSubmissions||10000),'number',true)}${['Ar','En','Ur'].map(l=>`<label>وصف ${l}<textarea name="description${l}">${esc(f['description'+l])}</textarea></label>`).join('')}</div><div id="builder-questions">${(f.fields||[{}]).map(questionCard).join('')}</div><button type="button" class="secondary" id="add-question">إضافة سؤال</button><button type="submit">حفظ المسودة</button></form>`);$('#add-question').onclick=()=>$('#builder-questions').insertAdjacentHTML('beforeend',questionCard({},document.querySelectorAll('.builder-question').length));$('#builder-form').onclick=e=>{if(e.target.closest('[data-remove-question]'))e.target.closest('.builder-question').remove();};$('#builder-form').onsubmit=submit(async d=>{const fields=[...document.querySelectorAll<HTMLFieldSetElement>('.builder-question')].map(q=>{const value=(n:string)=>(q.querySelector(`[name=${n}]`) as HTMLInputElement).value;return{id:value('id'),labelAr:value('labelAr'),labelEn:value('labelEn'),labelUr:value('labelUr'),type:value('type'),required:(q.querySelector('[name=required]') as HTMLInputElement).checked,options:value('options').split('\n').map(x=>x.trim()).filter(Boolean).map(line=>{const p=line.split('|').map(x=>x.trim());return{value:p[0],labelAr:p[1]||p[0],labelEn:p[2]||p[1]||p[0],labelUr:p[3]||p[1]||p[0]}})}});await api(id?`/forms/builder/${id}`:'/forms/builder',{...d,fields,maxSubmissions:Number(d.maxSubmissions),expiresAt:new Date(d.expiresAt).toISOString()},id?'PUT':'POST');$('#modal').close();await show('builder');});}
async function analyticsDialog(id:string){const [a,rows]=await Promise.all([api('/forms/analytics/'+id),api('/forms/responses/'+id)]);const form=cache.customForms?.find(x=>x.id===id);const keys=(form?.fields||[]).map(x=>x.id);if(!keys.length&&rows.length)keys.push(...Object.keys(rows[0].answers));modal('تحليل الردود',`<div class="stats"><div class="stat"><span>إجمالي الردود</span><strong>${a.totalResponses}</strong></div><div class="stat"><span>رد / ساعة منذ أول رد</span><strong>${a.responsesPerHour}</strong></div></div>${a.fields.map(q=>`<h3>${esc(q.label)}</h3>${q.options.map(o=>`<div class="distribution"><span>${esc(o.label)} · ${o.count} (${o.percentage}%)</span><progress max="100" value="${o.percentage}"></progress></div>`).join('')}`).join('')}${table(['وقت الرد',...keys.map(k=>form?.fields?.find(f=>f.id===k)?.labelAr||k)],rows.map(r=>`<tr><td>${esc(r.createdAt)}</td>${keys.map(k=>`<td>${esc(r.answers[k])}</td>`).join('')}</tr>`))}`);const headers=[...document.querySelectorAll<HTMLElement>('#modal .data-grid thead>tr:first-child>th')];keys.forEach((key,index)=>{if(headers[index+1])headers[index+1].dataset.numeric=String(form?.fields?.find(f=>f.id===key)?.type==='number')});
 const dialog=$('#modal');dialog.dataset.analyticsId=id;const known=new Set(rows.map(r=>r.id));let busy=false;
 const timer=setInterval(async()=>{
  if(!dialog.open||dialog.dataset.analyticsId!==id){clearInterval(timer);return;}if(busy)return;busy=true;
  try{const [fresh,replies]=await Promise.all([api('/forms/analytics/'+id),api('/forms/responses/'+id)]);if(!dialog.open||dialog.dataset.analyticsId!==id)return;
   const totals=dialog.querySelectorAll('.stats .stat strong');totals[0].textContent=String(fresh.totalResponses);totals[1].textContent=String(fresh.responsesPerHour);
   const breakdown=fresh.fields.flatMap(f=>f.options);dialog.querySelectorAll('.distribution').forEach((item,index)=>{const value=breakdown[index];if(value){item.querySelector('span').textContent=`${value.label} · ${value.count} (${value.percentage}%)`;item.querySelector('progress').value=value.percentage;}});
   const grid=dialog.querySelector('.data-grid');for(const r of replies){if(known.has(r.id))continue;known.add(r.id);grid.querySelector('tbody').insertAdjacentHTML('beforeend',`<tr><td>${esc(r.createdAt)}</td>${keys.map(k=>`<td>${esc(r.answers[k])}</td>`).join('')}</tr>`);}filterDataGrid(grid);
  }catch(error){if(dialog.open&&dialog.dataset.analyticsId===id)toast('تعذر تحديث التحليلات؛ ستتم إعادة المحاولة.',true);}finally{busy=false;}
 },10000);
}
async function inquiryCodeDialog(){modal('تسليم رمز استعلام خاص',`<form id="inquiry-code-form"><label>المندوب<select name="iqama">${riderOptions()}</select></label><label>النطاق<select name="scope"><option value="payslip">كشف راتب معتمد</option><option value="debt">بيان مديونية</option><option value="mobile">دخول بوابة المندوب</option></select></label><label class="check"><input type="checkbox" name="identityReviewed" required>راجعت هوية صاحب الطلب وطريقة تسليمه الرمز.</label><p>رمز لمرة واحدة صالح10دقائق. لا يُرسل تلقائيًا؛ سلّمه لصاحب الهوية عبر قناة موثوقة.</p><button>إصدار رمز</button></form>`);$('#inquiry-code-form').onsubmit=submit(async d=>{const r=await api('/inquiry-access',{...d,identityReviewed:true});modal('رمز الاستعلام الخاص',`<p>الإقامة: ${esc(r.iqama)}</p><div class="link-box">${esc(r.code)}</div><p>ينتهي: ${esc(r.expiresAt)}</p><a href="${esc(r.url)}" target="_blank" rel="noopener">صفحة الاستعلام</a>`);});}

document.addEventListener('click',async e=>{const b=(e.target as HTMLElement).closest<HTMLElement>('[data-enterprise]');if(!b)return;try{const id=b.dataset.id;switch(b.dataset.enterprise){
 case'finance-subtab':financeSubTab=b.dataset.subtab||'debts-list';await show('debts');break;
 case'open-statement':statementRiderIqama=b.dataset.iqama||'';financeSubTab='statement-360';await show('debts');break;
 case'rider-profile-360':await riderProfile360Dialog(b.dataset.iqama!);break;
 case'rider-enrich':{const res=await api('/riders/enrich-missing',{});await show('riders');toast(`تم استكمال حقول ${res.enrichedExisting} مندوب وإضافة ${res.addedFromSources} سجل من المصادر`);break;}
 case'debt-collect':debtCollectDialog(id!);break;
 case'debt-transfer-sp':debtTransferSponsorDialog(id!);break;
 case'opening-debt-import':openingDebtImportDialog();break;
 case'bulk-collect':bulkCollectDialog();break;
 case'vault-transfer':vaultTransferDialog();break;
 case'journal-detail':journalDetailDialog(id!);break;
 case'hr-advance-print':printVoucherDialog();break;
 case'print-voucher':printVoucherDialog(id);break;
 case'approval-decide':approvalDecideDialog(id!);break;
 case'notif-new':notifNewDialog();break;
 case'toggle-todo':await api(`/notifications/center/${id}/toggle-todo`,{});await show('debts');break;
 case'mobile-broadcast':mobileBroadcastDialog();break;
 case'firebase-config':await firebaseConfigDialog();break;
 case'share-statement-mobile':mobileBroadcastDialog(b.dataset.iqama||'ALL');break;
 case'toggle-promissory-flag':await api('/finance/features/promissory-notes',{enabled:b.dataset.enabled==='true'});await show('debts');toast('تم تحديث حالة موديول سندات الأمر');break;
 case'promissory-new':promissoryNewDialog();break;
 case'campaign-delete':await formDeleteDialog(id!,false);break;case'builder-delete':await formDeleteDialog(id!,true);break;case'campaign-edit':campaignEditDialog(id!);break;case'analytics-review':await show('responses');responseReview(id);break;case'copy':await navigator.clipboard.writeText(b.dataset.url!);toast('تم نسخ الرابط');break;case'qr':modal('رمز المشاركة QR',`<img class="qr-image" alt="QR لمشاركة النموذج" src="${esc(b.dataset.url)}">`);break;case'user-new':userDialog();break;case'user-edit':userDialog(id);break;case'debt-new':debtDialog();break;case'debt-edit':debtDialog(id);break;case'baseline':await baselineDialog();break;case'builder-new':builderDialog();break;case'builder-edit':builderDialog(id);break;case'builder-publish':if(confirm('نشر النموذج وتثبيت الأسئلة؟')){await api(`/forms/builder/${id}/publish`,{});await show('builder');}break;case'builder-close':if(confirm('إغلاق استقبال الردود؟')){await api(`/forms/builder/${id}/close`,{});await show('builder');}break;case'analytics':await analyticsDialog(id!);break;case'inquiry-code':await inquiryCodeDialog();break;
}}catch(error:any){toast(error.message,true);}});

let analyticsSelection='',analyticsRequest=0;
async function campaignEditDialog(id:string){const f=cache.campaigns.find(x=>x.id===id);const date=new Date(f.expiresAt);const local=new Date(date.getTime()-date.getTimezoneOffset()*60000).toISOString().slice(0,16);modal('تعديل النموذج',`<form id="campaign-edit-form" class="form-grid">${fieldInput('عنوان النموذج','title',f.title,'text',true)}${fieldInput('انتهاء الاستقبال','expiresAt',local,'datetime-local',true)}<label>الحالة<select name="status">${options([['open','مفتوح'],['closed','مغلق']],f.status)}</select></label><fieldset class="full choice-options"><legend>حقول إضافية مطلوبة عند تفعيلها</legend><label class="check"><input type="checkbox" name="city" ${f.fields?.includes('city')?'checked':''}>حقل المدينة (City)</label><label class="check"><input type="checkbox" name="phone" ${f.fields?.includes('phone')?'checked':''}>حقل الجوال (Phone)</label></fieldset><div class="notice full">رابط المشاركة والردود السابقة محفوظان. التغيير يسري على فتح النموذج وإرسال الردود التالية.</div><button>حفظ التعديلات</button></form>`);$('#campaign-edit-form').onsubmit=submit(async(d,form)=>{await api('/campaigns/'+id,{title:d.title,expiresAt:new Date(d.expiresAt).toISOString(),status:d.status,fields:['city','phone'].filter(f=>form.elements[f].checked)},'PUT');$('#modal').close();await show('forms');toast('تم تعديل النموذج مع الاحتفاظ برابطه')});}
function chartLabel(value:string){return systems[value]||({SponsoredInternal:'كفالة الشركة',External:'خارجي',Unknown:'غير محدد'}[value])||value||'غير محدد';}
function donutChart(values:any[],total:number){const colors=['#14b8a6','#f97360','#7986cb','#e0aa35','#46a4df'];let offset=0;return `<div class="donut-layout"><svg class="donut" viewBox="0 0 160 160" role="img" aria-label="توزيع الإجابات: ${total} رد"><circle cx="80" cy="80" r="58" fill="none" stroke="var(--raised)" stroke-width="22"/>${values.map((v,i)=>{const length=total?v.count/total*100:0;const start=offset;offset+=length;return `<circle class="chart-slice" tabindex="0" role="button" aria-label="${esc(chartLabel(v.value))}: ${v.count} (${v.percentage}%)" data-chart-detail="${esc(chartLabel(v.value))}: ${v.count} (${v.percentage}%)" cx="80" cy="80" r="58" pathLength="100" fill="none" stroke="${colors[i%colors.length]}" stroke-width="22" stroke-dasharray="${length} ${100-length}" stroke-dashoffset="${-start}" transform="rotate(-90 80 80)"><title>${esc(chartLabel(v.value))}: ${v.count} (${v.percentage}%)</title></circle>`}).join('')}<text x="80" y="78" text-anchor="middle" font-size="26">${total}</text><text x="80" y="98" text-anchor="middle" font-size="10">إجمالي الردود</text></svg><div class="chart-legend">${values.map((v,i)=>`<button class="legend-item" data-chart-detail="${esc(chartLabel(v.value))}: ${v.count} (${v.percentage}%)"><span class="legend-dot" style="--chart-color:${colors[i%colors.length]}"></span>${esc(chartLabel(v.value))}<strong>${v.count} · ${v.percentage}%</strong></button>`).join('')}</div></div><p class="analytics-summary" aria-live="polite">اضغط على شريحة أو مفتاح لون لعرض التفاصيل.</p>`;}
function barChart(values:any[]){if(!values.length)return '<p class="muted">لا توجد ردود بعد.</p>';return [...values].sort((a,b)=>b.count-a.count).map(v=>`<div class="bar-entry"><div class="bar-label"><span>${esc(chartLabel(v.value))}</span><strong>${v.count} · ${v.percentage}%</strong></div><svg viewBox="0 0 100 10" preserveAspectRatio="none" role="img" aria-label="${esc(chartLabel(v.value))}: ${v.count} (${v.percentage}%)"><title>${esc(chartLabel(v.value))}: ${v.count} (${v.percentage}%)</title><rect class="bar-track" width="100" height="10" rx="2"/><rect class="bar-fill" width="${Math.max(0,Math.min(100,Number(v.percentage)))}" height="10" rx="2"/></svg></div>`).join('');}
async function analyticsBody(value:string){if(!value)return empty('لا توجد نماذج بعد','أنشئ حملة اختيار أو انشر نموذجًا مخصصًا.');const [kind,id]=value.split(':');const custom=kind==='custom';const a=await api(custom?'/forms/analytics/'+id:'/campaigns/'+id+'/analytics');const total=a.totalResponses,percent=(n:number)=>total?(100*n/total).toFixed(1):'0.0';const f=cache.analyticsForms.find(x=>x.value===value);let choice=custom?(a.fields[0]?.options||[]):['tiers','commission'].map(v=>a.workSystem.find(x=>x.value===v)||{value:v,count:0,percentage:0}).concat(a.workSystem.filter(x=>!['tiers','commission'].includes(x.value)));if(custom)choice=choice.map(v=>({...v,value:v.label}));
return `<div class="stats analytics-kpis">${[['إجمالي الردود',total,custom?`${a.responsesPerHour} رد / ساعة`:'ردود الحملة الحالية'],['المعتمدة',custom?'—':a.approved,custom?'النموذج المخصص لا يمر باعتماد اختيار':percent(a.approved)+'% من الردود'],['قيد المراجعة',custom?'—':a.pending,custom?'عرض جميع الردود من التفاصيل':percent(a.pending)+'% من الردود'],['الردود المعدلة',custom?'—':a.edited,custom?'مخطط الأسئلة ثابت بعد النشر':`${a.rejected} رد مرفوض`]].map(([label,note,hint])=>`<article class="stat"><div class="label">${label}</div><strong>${note}</strong><div class="hint">${hint}</div></article>`).join('')}</div><div class="chart-grid"><article class="panel chart-card"><h2>${custom?esc(a.fields[0]?.label||'توزيع الإجابات'):'نظام العمل المختار'}</h2>${donutChart(choice,total)}</article><article class="panel chart-card"><h2>${custom?'الأسئلة الإضافية':'نوع المندوب — كفالة الشركة وخارجي'}</h2>${custom?a.fields.slice(1).map(q=>`<h3>${esc(q.label)}</h3>${barChart(q.options.map(o=>({...o,value:o.label})))}`).join('')||'<p class="muted">لا توجد أسئلة اختيار إضافية.</p>':barChart(a.engagementType)}</article>${!custom?`<article class="panel chart-card"><h2>توزيع المدن</h2>${barChart(a.city)}</article><article class="panel chart-card"><h2>أحدث الردود الفردية</h2><div class="recent-list">${a.recent.map(r=>`<div class="recent-item"><div><strong>${esc(r.name)}</strong><small>${esc(r.iqama)} · ${esc(systems[r.workSystem])}</small><small>${esc(new Date(r.updatedAt||r.createdAt).toLocaleString('ar-SA'))}</small></div><button class="secondary" data-enterprise="analytics-review" data-id="${esc(r.id)}">مراجعة الرد</button></div>`).join('')||'<p class="muted">لا توجد ردود بعد.</p>'}</div></article>`:`<article class="panel analytics-extra"><h2>الردود الفردية والتصدير</h2><button data-enterprise="analytics" data-id="${esc(id)}">عرض الردود التفصيلية</button></article>`}</div>`;}
async function formAnalyticsView(){const [campaigns,forms]=await Promise.all([api('/campaigns'),api('/forms/catalog')]);cache.customForms=forms;cache.analyticsForms=[...campaigns.map(f=>({value:'campaign:'+f.id,title:f.title})),...forms.map(f=>({value:'custom:'+f.id,title:f.titleAr}))];if(!cache.analyticsForms.some(x=>x.value===analyticsSelection))analyticsSelection=cache.analyticsForms[0]?.value||'';return heading('تحليل النماذج','ملخص مرئي وتوزيع الردود؛ يتحدث تلقائيًا كل10ثوانٍ.')+`<label class="analytics-selector">اختر الحملة أو النموذج<select id="analytics-campaign-select">${options(cache.analyticsForms.map(f=>[f.value,f.title]),analyticsSelection)}</select></label><div id="analytics-report">${await analyticsBody(analyticsSelection)}</div>`;}
async function updateAnalytics(){const selected=analyticsSelection,request=++analyticsRequest;const html=await analyticsBody(selected);if(active==='analytics'&&selected===analyticsSelection&&request===analyticsRequest&&$('#analytics-report'))$('#analytics-report').innerHTML=html;}
document.addEventListener('click',e=>{const target=(e.target as Element).closest<HTMLElement>('[data-chart-detail]');if(target){const card=target.closest('.chart-card');const summary=card?.querySelector('.analytics-summary');if(summary)summary.textContent=target.dataset.chartDetail;}});
document.addEventListener('keydown',e=>{if((e.key==='Enter'||e.key===' ')&&(e.target as Element).matches('circle[data-chart-detail]')){e.preventDefault();(e.target as Element).dispatchEvent(new MouseEvent('click',{bubbles:true}));}});

async function formDeleteDialog(id:string,custom:boolean){const path=(custom?'/forms/builder/':'/campaigns/')+encodeURIComponent(id);const preview=await api(path+'/delete-preview');modal('حذف النموذج',`<form id="form-delete-confirm"><p>النموذج: <strong>${esc(preview.title)}</strong></p><div class="notice warn">سيُحذف هذا النموذج نهائيًا مع جميع ردوده (${preview.responseCount} رد)، ويتوقف رابط المشاركة عن العمل.</div><p class="muted">سجل العمليات ودليل المناديب والدفاتر المالية محفوظة.</p><label class="check"><input type="checkbox" name="confirm" required>أؤكد حذف النموذج وجميع ردوده نهائيًا.</label><div class="actions"><button type="submit" class="danger">حذف النموذج نهائيًا</button><button type="button" class="secondary" data-action="close-modal">إلغاء</button></div></form>`);$('#form-delete-confirm').onsubmit=submit(async(_d,form)=>{try{await api(path,{confirm:form.elements.confirm.checked,fingerprint:preview.fingerprint},'DELETE')}catch(error){if(error.status===409){$('#modal').close();await formDeleteDialog(id,custom)}throw error}$('#modal').close();if(!custom&&responseCampaign===id)responseCampaign='';if(analyticsSelection===(custom?'custom:':'campaign:')+id)analyticsSelection='';await show(custom?'builder':'forms');toast('تم حذف النموذج وردوده نهائيًا')});}
