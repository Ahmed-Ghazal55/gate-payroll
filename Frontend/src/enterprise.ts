const themeNames={dark:'داكن',light:'فاتح',dim:'متوسط'};
function setTheme(value:string){const theme=Object.hasOwn(themeNames,value)?value:'light';document.documentElement.dataset.theme=theme;try{localStorage.setItem('gate-theme',theme)}catch{}}
try{setTheme(localStorage.getItem('gate-theme')||'light')}catch{setTheme('light')}
function themeControl(){return `<select class="theme-control" aria-label="مظهر الواجهة" data-theme-select>${options(Object.entries(themeNames),document.documentElement.dataset.theme)}</select>`;}
function can(permission:string){return userRole==='Admin'||userRole==='SuperAdmin'||userPermissions.includes(permission)}
function defaultView(){return ['dashboard','responses','riders','debts'].find(visibleView)||'riders'}
function visibleView(view:string){const permissions:Record<string,string>={dashboard:'payroll.view',riders:'riders.view',payroll:'payroll.view',ledger:'payroll.view',debts:'debts.view',policies:'policies.view',responses:'forms.view',analytics:'forms.view',forms:'forms.view',builder:'forms.manage',users:'users.manage',imports:'imports.view'};return view==='audit'?(userRole==='Admin'||userRole==='SuperAdmin'):!!permissions[view]&&can(permissions[view])}

function navIcon(id:string){const paths:Record<string,string>={dashboard:'M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z',riders:'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8M17 4a4 4 0 0 1 0 8M22 21v-2a4 4 0 0 0-3-4',payroll:'M6 3h12v18l-3-2-3 2-3-2-3 2zM9 7h6M9 11h6M9 15h3',forms:'M10 13a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-2 2M14 11a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l2-2',ledger:'M4 3h16v18H4zM8 7h8M8 11h8M8 15h4',users:'M12 3l8 4v6c0 4-8 8-8 8s-8-4-8-8V7zM9 12l2 2 4-4',audit:'M12 8v5l3 2M21 12a9 9 0 1 1-9-9',debts:'M3 7h18v13H3zM3 7l3-4h12l3 4M15 11h6v5h-6z',imports:'M12 16V3M7 8l5-5 5 5M4 15v6h16v-6',policies:'M4 6h16M4 12h16M4 18h16',responses:'M5 12l4 4L19 6'};return `<svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${paths[id]||paths.forms}"/></svg>`;}
document.addEventListener('change',e=>{const target=e.target as HTMLSelectElement;if(target.matches('[data-theme-select]'))setTheme(target.value);});
const fieldInput=(label:string,key:string,value='',type='text',required=false)=>`<label>${esc(label)}<input name="${key}" type="${type}" value="${esc(value)}" ${required?'required':''}></label>`;
const roleLabels:Record<string,string>={
 Admin:'مدير النظام العام (Super Admin)',
 SuperAdmin:'المدير العام / الإدارة العليا',
 Accountant:'محاسب مالي ومسؤول خزينة',
 FinancialAccountant:'رئيس الحسابات والخزينة المركزية',
 HRSpecialist:'أخصائي الموارد البشرية والعلاقات الحكومية',
 OperationsManager:'مدير التشغيل والمنصات',
 PlatformsSupervisor:'مشرف منصات وعهدة صندوق جاهز',
 FleetSupervisor:'مشرف الحركة والأسطول والورشة',
 HousingSupervisor:'مشرف السكن والإعاشة',
 WarehouseOfficer:'أمين المستودع والعهد والتجهيزات',
 Reviewer:'مراجع البيانات والنماذج'
};
const statusLabels:Record<string,string>={Active:'نشط',OnLeave:'في إجازة',Archived:'مؤرشف',NeedsReview:'يحتاج مراجعة',active:'نشط',suspended:'موقوف',settled:'مسدد',paused:'متوقف',pending_approval:'بانتظار تصديق القسم الآخر',cancelled:'ملغي'};
const allocationLabels:Record<string,string>={RiderFull:'تحمل المندوب 100% (ذمة مدينة)',CompanyFull:'تحمل الشركة 100% (مصروف)',SharedSplit:'تحمل جزئي مشترك (شركة + مندوب)'};

let financeSubTab='debts-list';
let statementRiderIqama='';
let statementSponsorFilter='';

// فلترة الحسابات والصناديق ديناميكياً حسب نوع العملية لمنع ظهور صندوق جاهز أو رسوم جاهز أو بترو آب في طلبات الموارد البشرية أو السلف
function filterVaultsForCategory(vaults:any[],categoryCode:string,operationKind:'disbursement'|'collection',preferredSponsorId?:string,platform?:string):any[]{
 const catalog=cache.financeCatalog||{categories:[]};
 const cat=(catalog.categories||[]).find((c:any)=>c.code===categoryCode)||{code:categoryCode,sector:'HR'};
 const sector=cat.sector||'HR';

 const filtered=(vaults||[]).filter((v:any)=>{
  const vKind=v.vaultKind||(v.code?.startsWith('SUP.')?'JahezCashbox':v.code?.startsWith('BANK.')?'Bank':v.code?.startsWith('PETRO.')?'PetroApp':'MainInternal');
  const vPurpose=v.vaultPurpose||(v.code?.endsWith('.FEE')?'JahezFee':v.code?.endsWith('.DEBT')?'DebtAndHr':'GeneralAndSettlement');
  const dir=v.allowedDirections||(v.code?.endsWith('.FEE')||v.code?.startsWith('PETRO.')?'InOnly':'Dual');

  // 1. حسابات رسوم فتح حسابات جاهز (*.FEE) لا تظهر أبداً في السلف أو طلبات HR أو المديونيات
  if(categoryCode==='PlatformTransferredDebt')return operationKind==='collection'&&platform==='Jahez'&&['JahezSettlement','GeneralAndSettlement'].includes(vPurpose);
  if(['JahezFee','GeneralAndSettlement','JahezSettlement'].includes(vPurpose)&&vKind!=='JahezCashbox')return false;
  if(vPurpose==='JahezFee')return false;

  // 2. في حالة الصرف/التمويل، يُمنع أي حساب طبيعته قبض فقط (InOnly مثل بترو آب)
  if(operationKind==='disbursement'&&dir==='InOnly')return false;

  // 3. عزل صناديق وخزائن جاهز (SUP.*) تماماً عن طلبات HR والسلف والأسطول والسكن والجزاءات الإدارية
  if(vKind==='JahezCashbox'){
   return operationKind==='collection'&&categoryCode==='PlatformTransferredDebt'&&platform==='Jahez';
  }

  // 4. عزل حسابات مقاصة بترو آب (PETRO.*) عن طلبات HR والسلف
  if(vKind==='PetroApp'){
   return operationKind==='collection'&&categoryCode==='FuelOverage';
  }

  // 5. تخصيص السلفة النقدية مقابل السلفة البنكية عند الصرف
  if(operationKind==='disbursement'){
   if(categoryCode==='CashAdvance'&&vKind==='Bank')return false;
   if(categoryCode==='BankAdvance'&&vKind!=='Bank')return false;
  }

  if(cat.allowedVaultKinds&&!cat.allowedVaultKinds.includes(vKind)&&!(operationKind==='collection'&&['Advance','CashAdvance','BankAdvance','CashCustody'].includes(categoryCode)&&['MainInternal','Bank'].includes(vKind)))return false;
  return true;
 });

 return filtered.sort((a:any,b:any)=>{
  const aSp=preferredSponsorId&&a.defaultSponsorId===preferredSponsorId?0:1;
  const bSp=preferredSponsorId&&b.defaultSponsorId===preferredSponsorId?0:1;
  if(aSp!==bSp)return aSp-bSp;
  const aDebt=(sector==='HR'||sector==='Advances'||sector==='Fleet')&&a.vaultPurpose==='DebtAndHr'?0:1;
  const bDebt=(sector==='HR'||sector==='Advances'||sector==='Fleet')&&b.vaultPurpose==='DebtAndHr'?0:1;
  return aDebt-bDebt;
 });
}

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
   userRole==='Admin'||userRole==='SuperAdmin'
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
     ${userRole==='Admin'||userRole==='SuperAdmin'?`<button class="small ghost" data-action="rider-edit" data-id="${esc(r.id)}">تعديل</button>`:''}
    </div></td>
   </tr>`)
  )}</div>`;
 }
 if(view==='forms'){const forms=await api('/campaigns');cache.campaigns=forms;return heading('النماذج والروابط','روابط محفوظة قابلة للنسخ وQR دون إظهار بيانات المناديب.',`<button data-action="campaign-new">إنشاء حملة اختيار</button><button class="secondary" data-view="builder">مصمم النماذج</button>`)+`<div id="new-link"></div><div class="panel">${table(['الحملة','الانتهاء','الحالة','الرابط',''],forms.map(f=>`<tr><td>${esc(f.title)}</td><td>${esc(f.expiresAt.slice(0,10))}</td><td>${esc(f.status)}</td><td><span class="link-label" dir="ltr">${esc(f.publicUrl)}</span></td><td><button data-enterprise="copy" data-url="${esc(f.publicUrl)}">نسخ الرابط</button><button class="secondary" data-enterprise="qr" data-url="${esc(f.qrCodeUrl)}">QR</button><button class="secondary" data-enterprise="campaign-edit" data-id="${esc(f.id)}">تعديل النموذج</button>${userRole==='Admin'||userRole==='SuperAdmin'?`<button class="danger" data-enterprise="campaign-delete" data-id="${esc(f.id)}">حذف النموذج</button>`:''}${f.status==='open'?`<button class="ghost" data-action="campaign-close" data-id="${f.id}">إغلاق</button>`:''}</td></tr>`))}</div>`;}
 if(view==='users'){
  const [users,rolesCat]=await Promise.all([api('/users'),api('/roles-catalog').catch(()=>({roles:[],permissions:[],departments:[]}))]);
  cache.users=users;cache.rolesCatalog=rolesCat;
  const rolesList=rolesCat.roles||[];
  const permsList=rolesCat.permissions||[];
  return heading(
   'إدارة المستخدمين والأدوار والصلاحيات التفصيلية (RBAC)',
   'هيكلة صلاحيات ذرية دقيقة مرتبطة بالأدوار التشغيلية والأقسام والرتب الوظيفية، مع فحص إلزامي في الخادم وإلغاء فوري للجلسات عند الإيقاف.',
   `<button data-enterprise="user-new">＋ إنشاء مستخدم وتعيين الدور والصلاحيات</button>`
  )+`
  <div class="panel" style="margin-bottom:16px">
   <h2>1. الحسابات والمستخدمون المسجلون (${users.length})</h2>
   ${table(
    ['اسم المستخدم','الدور الوظيفي','القسم','الرتبة الوظيفية','الصلاحيات المفعلة','الحالة','تاريخ الإنشاء','آخر دخول',''],
    users.map((u:any)=>`<tr>
     <td><strong>${esc(u.username)}</strong></td>
     <td><strong>${esc(u.roleNameAr||roleLabels[u.role]||u.role)}</strong><div class="muted small number">${esc(u.role)}</div></td>
     <td>${esc(u.departmentNameAr||u.department||'—')}</td>
     <td>${badge(`رتبة ${u.rankLevel||1}`,'good')}</td>
     <td class="number">${(u.permissions||[]).length} صلاحية ${(u.customPermissions||[]).length?`<span class="muted small">(+${u.customPermissions.length} مخصصة)</span>`:''}</td>
     <td>${badge(statusLabels[u.status]||u.status,u.status==='active'?'good':'warn')}</td>
     <td class="small number">${esc((u.createdAt||'').slice(0,10))}</td>
     <td class="small number">${esc(u.lastLoginAt?u.lastLoginAt.slice(0,16):'—')}</td>
     <td><button class="secondary small" data-enterprise="user-edit" data-id="${esc(u.id)}">تعديل الصلاحيات</button></td>
    </tr>`)
   )}
  </div>
  <div class="panel" style="margin-bottom:16px">
   <h2>2. مصفوفة الأدوار التشغيلية والرقابية المعتمدة (${rolesList.length} أدوار)</h2>
   ${table(
    ['كود الدور','المسمى الوظيفي','القسم الافتراضي','الرتبة الافتراضية','الوصف التشغيلي والاختصاص','عدد الصلاحيات الافتراضية'],
    rolesList.map((r:any)=>`<tr>
     <td class="number"><strong>${esc(r.code)}</strong></td>
     <td><strong>${esc(r.nameAr)}</strong></td>
     <td class="number">${esc(r.defaultDepartment)}</td>
     <td>رتبة ${r.defaultRankLevel}</td>
     <td class="small">${esc(r.summaryAr)}</td>
     <td class="number">${(r.defaultPermissions||[]).length} صلاحية</td>
    </tr>`)
   )}
  </div>
  <div class="panel">
   <h2>3. دليل الصلاحيات الذرية الدقيقة والشرح التشغيلي (${permsList.length} صلاحية)</h2>
   ${table(
    ['كود الصلاحية (Permission Key)','الوحدة / القسم','اسم الصلاحية','الشرح التشغيلي وما تتحكم به في النظام'],
    permsList.map((p:any)=>`<tr>
     <td class="number"><code>${esc(p.code)}</code></td>
     <td>${badge(p.moduleAr,'')}</td>
     <td><strong>${esc(p.nameAr)}</strong></td>
     <td class="small muted">${esc(p.descriptionAr)}</td>
    </tr>`)
   )}
  </div>`;
 }
 if(view==='debts')return await renderFinancialSystemHub();
 if(view==='ledger'){cache.riders=await api('/riders');const rows=await api('/ledger');return heading('دفتر الحسابات','حركات المسيرات المعتمدة والأقساط والدفعات، دون إدخال مباشر يتجاوز الاعتماد.',`<button data-enterprise="inquiry-code">رمز استعلام للمندوب</button>`)+`<div class="panel">${table(['الإقامة','الفترة','الحركة','المبلغ','المرجع'],rows.map(x=>`<tr><td>${esc(x.iqama)}</td><td>${esc(x.period)}</td><td>${esc(x.kind)}</td><td class="number">${money(x.amount)}</td><td>${esc(x.sourceRef)}</td></tr>`))}</div>`;}
 if(view==='builder'){const forms=await api('/forms/builder');cache.customForms=forms;return heading('مصمم النماذج','أسئلة بثلاث لغات؛ النشر يثبّت المخطط ويحفظ رابط المشاركة.',`<button data-enterprise="builder-new">نموذج جديد</button>`)+`<div class="panel">${table(['العنوان','الحالة','الحد الأقصى','الانتهاء',''],forms.map(f=>`<tr><td>${esc(f.titleAr)}</td><td>${esc(f.status)}</td><td>${f.maxSubmissions}</td><td>${esc(f.expiresAt.slice(0,10))}</td><td>${f.status==='draft'?`<button data-enterprise="builder-publish" data-id="${f.id}">نشر</button><button class="secondary" data-enterprise="builder-edit" data-id="${f.id}">تحرير</button>`:`<button data-enterprise="copy" data-url="${esc(f.publicUrl)}">نسخ الرابط</button><button class="secondary" data-enterprise="qr" data-url="${esc(f.qrCodeUrl)}">QR</button><button class="secondary" data-enterprise="analytics" data-id="${f.id}">التحليلات</button>${f.status==='open'?`<button class="ghost" data-enterprise="builder-close" data-id="${f.id}">إغلاق</button>`:''}`}<button class="danger" data-enterprise="builder-delete" data-id="${esc(f.id)}">حذف النموذج</button></td></tr>`))}</div>`;}
 if(view==='analytics')return await formAnalyticsView();
 return '';
}

async function renderFinancialSystemHub():Promise<string>{
 const [debts,riders,catalog,sponsors,accounts,journals,approvals,notifCenter,broadcasts,promissory,handovers]=await Promise.all([
  api('/debts'),api('/riders'),api('/finance/catalog'),can('treasury.view')?api('/finance/sponsors'):Promise.resolve([]),can('treasury.view')?api('/finance/accounts'):Promise.resolve([]),
  can('treasury.view')?api('/finance/journals'):Promise.resolve([]),can('approvals.view')?api('/approvals'):Promise.resolve([]),can('approvals.view')?api('/notifications/center'):Promise.resolve({}),can('mobile.broadcast')?api('/rider-mobile/broadcasts'):Promise.resolve([]),api('/promissory-notes'),can('treasury.view')?api('/finance/jahez-handovers'):Promise.resolve([])
 ]);
 cache.debts=debts;cache.riders=riders;cache.financeCatalog=catalog;cache.sponsors=sponsors;cache.accounts=accounts;
 cache.journals=journals;cache.approvals=approvals;cache.notifCenter=notifCenter;cache.broadcasts=broadcasts;cache.promissory=promissory;cache.handovers=handovers;
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
 ].filter(([id])=>id!=='vaults-coa'||can('treasury.view')).filter(([id])=>id!=='approvals-notif'||can('approvals.view'));
 if(!subTabs.some(([id])=>id===financeSubTab))financeSubTab='debts-list';

 const topActions=`${userPermissions.some(p=>p.startsWith('debts.create.'))||userRole==='Admin'||userRole==='SuperAdmin'?'<button data-enterprise="debt-new">＋ تسجيل مديونية / خدمة HR / سلفة</button>':''}
 ${can('debts.import_opening')?'<button class="good" data-enterprise="opening-debt-import">📥 رفع رصيد أول المدة وشيت المديونيات</button>':''}
 ${can('debts.collect')?'<button class="secondary" data-enterprise="bulk-collect">شيت التحصيل المجمع (Excel / مباشر)</button>':''}
 ${can('treasury.transfer')?'<button class="secondary" data-enterprise="vault-transfer">تحويل بين الصناديق والبنوك</button>':''}
 ${can('treasury.jahez.handover')?'<button class="secondary" data-enterprise="jahez-handover">🔄 تسليم وتصفير عهدة جاهز</button>':''}
 <button class="secondary" data-enterprise="hr-advance-print">نموذج سلفة نقدية / سند قبض</button>${can('treasury.opening')?'<button class="secondary" data-enterprise="treasury-opening">مطابقة الأرصدة الافتتاحية</button>'+((catalog.sponsors||[]).some((s:any)=>s.code==='TR')?'<button class="secondary" data-enterprise="legacy-cash-allocation">توزيع خزنة التجارية التاريخية</button>':''):''}`;

 const sponsorCards=sponsors.map((sp:any)=>`
  <article class="stat">
   <div class="label">${esc(sp.shortNameAr)} — <span class="number">${esc(sp.unifiedNumber)}</span></div>
   <strong class="money-positive">${money(sp.receivablesBalance)} <small style="font-size:12px">ر.س ذمم مدينة</small></strong>
   <div class="hint">الصندوق الداخلي: <b class="number">${money(sp.mainVaultBalance??sp.vaultBalance)}</b> · خزنة جاهز: <b class="number">${money(sp.jahezBoxBalance||0)}</b> · البنك: <b class="number">${money(sp.bankBalance||0)}</b> · مصروفات: <b class="number">${money(sp.expensesTotal)}</b> ر.س</div>
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
    <strong>القاعدة المحاسبية وعزل الصناديق:</strong>
    <b>الصندوق الداخلي الرئيسي (1110 / MAIN)</b> تتفرع تحته حسابات المديونيات والسلف وطلبات الموارد البشرية (<code>MAIN.*.DEBT</code>) وحسابات التحصيلات والتصفيات (<code>MAIN.*.SET</code>) وحسابات رسوم فتح حسابات جاهز (<code>MAIN.*.FEE</code>) وحساب مؤسسة التجارية الموحد (<code>MAIN.TR</code>). أما <b>صندوق جاهز (1120 / SUP.*)</b> فهو خاص حصرياً بمعاملات وأرصدة تطبيق وشركة جاهز، ولا يُسمح بظهوره أو استخدامه في السلف أو طلبات الموارد البشرية (HR).
   </div>
   <div class="panel">
    <div class="panel-title">
     <h2>سجل المديونيات، خدمات الموارد البشرية (HR)، السلف، والأقساط</h2>
     <div class="actions"><a class="button secondary" href="/api/debts/bulk-collect/template">تنزيل قالب التحصيل المجمع (Excel)</a></div>
    </div>
    ${table(
     ['المندوب / الموظف','الإقامة','البند / الخدمة','جهة التحمل','إجمالي التكلفة','حصة الشركة (مصروف)','ذمة المندوب','المحصل','إعفاء إداري','المتبقي','القسط','السجل الدائن','الصندوق','القيد','الحالة',''],
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
       <td class="number">${esc(d.paymentVaultCode||(d.entryMode==='Accrual'?'إثبات غير نقدي':'غير مسجل'))}</td>
       <td class="number">${esc(d.journalNumber||'—')}</td>
       <td>${badge(statusLabels[d.status]||d.status,d.status==='settled'?'good':d.status==='pending_approval'?'warn':'')}</td>
       <td>
        <div class="row-actions">
         ${canAct&&can('debts.collect')?`<button class="small good" data-enterprise="debt-collect" data-id="${esc(d.id)}">تحصيل / إعفاء</button>`:''}
         ${canAct&&can('debts.reschedule')?`<button class="small secondary" data-enterprise="debt-edit" data-id="${esc(d.id)}">جدولة</button>`:''}
         ${canAct&&can('debts.transfer_sponsor')?`<button class="small secondary" data-enterprise="debt-transfer-sp" data-id="${esc(d.id)}">شراء/نقل لسجل</button>`:''}
         ${can('treasury.liability.settle')&&d.entryMode==='Accrual'&&d.recognitionAccountCode==='2130'&&Number(d.totalCostAmount||d.totalAmount)>Number(d.supplierPaidAmount||0)?`<button class="small secondary" data-enterprise="supplier-pay" data-id="${esc(d.id)}">سداد الالتزام</button>`:''}
         ${can('treasury.opening')&&!d.journalEntryId&&!d.openingReconciliationId?`<button class="small secondary" data-enterprise="debt-reconcile" data-id="${esc(d.id)}">مطابقة الافتتاح</button>`:''}
         <button class="small ghost" data-enterprise="open-statement" data-iqama="${esc(d.iqama)}">كشف 360°</button>
         <button class="small ghost" data-enterprise="print-voucher" data-id="${esc(d.id)}">سند/نموذج</button>
        </div>
       </td>
      </tr>`;
     })
    )}
   </div>`;
 } else if(financeSubTab==='vaults-coa'){
  const handoverTable=table(['المستند','السجل','الحالة','التصفيات','الرسوم','مقدم العهدة','المستلم',''],handovers.map((h:any)=>`<tr><td>${esc(h.sourceRef)}</td><td>${esc(h.sponsorNameAr)}</td><td>${badge(h.status==='pending'?'بانتظار الاستلام':h.status==='approved'?'مستلم':'مرفوض',h.status==='approved'?'good':'warn')}</td><td class="number">${money(h.settlementAmount)}</td><td class="number">${money(h.feeAmount)}</td><td>${esc(h.submittedBy)}</td><td>${esc(h.decidedBy||'—')}</td><td>${h.status==='pending'&&can('treasury.jahez.receive')?`<button class="small good" data-enterprise="jahez-receive" data-id="${esc(h.id)}">عد واستلام / رفض</button>`:''}</td></tr>`));
  const mainRoot=accounts.find((a:any)=>a.code==='1110');
  const jahezRoot=accounts.find((a:any)=>a.code==='1120');
  const bankRoot=accounts.find((a:any)=>a.code==='1130');
  const petroRoot=accounts.find((a:any)=>a.code==='1140');
  bodyHtml=`
   <div class="stats">
    <article class="stat"><div class="label">1110 — الصندوق الداخلي الرئيسي العام (MAIN)</div><strong class="number money-positive">${money(mainRoot?.netBalance||0)} ر.س</strong><div class="hint">يضم حسابات المديونيات والسلف (1111) وتصفيات جاهز (1112) والرسوم (1113) وصندوق التجارية (MAIN.TR)</div></article>
    <article class="stat"><div class="label">1120 — صندوق / خزنة كاش جاهز (SUP)</div><strong class="number">${money(jahezRoot?.netBalance||0)} ر.س</strong><div class="hint">خاص حصرياً بمعاملات تطبيق جاهز ويُصفّر للصندوق الداخلي أو البنك</div></article>
    <article class="stat"><div class="label">1130 — حسابات البنوك الرسمية (BANK)</div><strong class="number money-positive">${money(bankRoot?.netBalance||0)} ر.س</strong><div class="hint">حسابات مستقلة لكل سجل؛ العمليات بالنيابة لها جاري موثق</div></article>
    <article class="stat"><div class="label">1140 — مقاصة وقود بترو آب (PETRO)</div><strong class="number">${money(petroRoot?.netBalance||0)} ر.س</strong><div class="hint">حساب قبض ومقاصة وقود فقط (InOnly)</div></article>
   </div>
   <div class="panel"><h2>تسليمات جاهز — لا تنتقل النقدية قبل استلام المالية وعدها</h2>${handoverTable}</div>
   <div class="panel">
    <div class="panel-title">
     <h2>شجرة الحسابات الهرمية، الصندوق الداخلي الرئيسي وفروعه، خزنة جاهز، البنوك، ومصروفات HR</h2>
     <div class="actions">
      ${can('treasury.jahez.handover')?'<button class="good" data-enterprise="jahez-handover">🔄 تسليم وتصفير عهدة جاهز للصندوق الداخلي</button>':''}
      ${can('treasury.jahez.manage')?'<button class="secondary" data-enterprise="jahez-fee">قبض رسوم فتح حساب جاهز</button>':''}
      ${can('treasury.transfer')?'<button class="secondary" data-enterprise="jahez-transfer">تحويل عهدة جاهز</button><button class="secondary" data-enterprise="jahez-platform-settle">تسوية مع شركة جاهز</button><button class="secondary" data-enterprise="jahez-fee-deposit">إيداع حصيلة رسوم الشركة</button>':''}
${can('treasury.transfer')?'<button class="secondary" data-enterprise="vault-transfer">تحويل نقدية الشركة</button>':''}
     </div>
    </div>
    ${table(
     ['رمز الحساب','الأب','اسم الحساب / الصندوق','التصنيف والاتجاه','الطبيعة','السجل','إجمالي المدين','إجمالي الدائن','الرصيد الصافي','النطاق التشغيلي المعتمد'],
     accounts.map((a:any)=>{
      const depth=a.parentCode?((a.parentCode==='1111'||a.parentCode==='1112'||a.parentCode==='1113')?2:1):0;
      const prefix=depth===2?'↳ ↳ ':depth===1?'↳ ':'';
      return `<tr>
       <td class="number"><strong>${esc(a.code)}</strong></td>
       <td class="number muted small">${esc(a.parentCode||'رئيسي')}</td>
       <td>${prefix}<strong>${esc(a.nameAr)}</strong> ${a.isGroup?badge('حساب تجميعي رئيسي',''):a.isVault?badge(a.vaultKind==='JahezCashbox'?'خزنة تطبيق جاهز':a.vaultKind==='Bank'?'حساب بنكي':a.vaultKind==='PetroApp'?'مقاصة وقود':'صندوق داخلي','good'):''}</td>
       <td class="small">${esc(a.accountType)} · ${badge(a.allowedDirectionsAr||'قبض وصرف',a.allowedDirections==='InOnly'?'warn':'')}</td>
       <td>${badge(a.naturalSideAr,a.naturalSide==='Debit'?'':'warn')}</td>
       <td>${esc(a.defaultSponsorNameAr)}</td>
       <td class="number">${money(a.debitTotal)}</td>
       <td class="number">${money(a.creditTotal)}</td>
       <td class="number ${a.netBalance<0?'money-negative':'money-positive'}"><strong>${money(a.netBalance)}</strong></td>
       <td class="small muted">${esc(a.operationalScopeAr||a.ruleExplanationAr)}</td>
      </tr>`;
     })
    )}
   </div>
   <div class="panel">
    <h2>دفتر القيود المحاسبية المزدوجة (Double-Entry Journal Entries)</h2>
    ${table(
     ['رقم القيد','التاريخ','الفترة','نوع المعاملة','البيان','السجل المنشئ','السجل الدائن','إجمالي المدين','إجمالي الدائن','المرجع',''],
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
    <article class="stat"><div class="label">إجمالي أصل المديونيات على المندوب (مدين)</div><strong class="money-negative">${money(st.totalPrincipalDebt)}</strong><div class="hint">تحملات الشركة لخدمات HR: ${money(st.totalCompanyBorneCost||0)} ر.س</div></article>
    <article class="stat"><div class="label">إجمالي المسدد نقداً وبنكياً ومن الراتب (دائن)</div><strong class="money-positive">${money(st.totalPaidAmount)}</strong><div class="hint">تخفيض مباشر للذمة المدينة</div></article>
    <article class="stat"><div class="label">إجمالي الإعفاءات الإدارية المعتمدة (دائن)</div><strong>${money(st.totalWaivedAmount)}</strong><div class="hint">تُقيد في ح/ مصروف إعفاءات 5202</div></article>
    <article class="stat"><div class="label">صافي المديونية المتبقية حالياً</div><strong class="${st.totalRemainingDebt>0?'money-negative':'money-positive'}">${money(st.totalRemainingDebt)}</strong><div class="hint">رصيد دفتر الرواتب العام: ${st.payrollVisible===false?'خارج صلاحية الدور':money(st.payrollLedgerBalance)+' ر.س'}</div></article>
   </div>
   <div class="panel">
    <div class="panel-title">
     <h2>1. توزيع ذمم المندوب (${esc(st.riderName)} — ${esc(st.iqama)}) وتكاليف HR حسب السجلات التجارية</h2>
     <div class="actions">
      <button class="secondary no-print" data-action="print">طباعة كشف الحساب الرسمي</button>
      <button class="secondary no-print" data-enterprise="share-statement-mobile" data-iqama="${esc(st.iqama)}">مشاركة الكشف داخل بوابة المندوب</button>
     </div>
    </div>
    ${table(
     ['السجل التجاري','إجمالي أصل الدين على المندوب (مدين)','حصة تحمل الشركة (مصروف HR)','المسدد (دائن)','الإعفاء الإداري (دائن)','الرصيد المتبقي للسجل'],
     st.bySponsor.map((b:any)=>`<tr>
      <td><strong>${esc(b.sponsorNameAr)}</strong></td>
      <td class="number">${money(b.totalPrincipal)}</td>
      <td class="number">${money(b.totalCompanyShare||0)}</td>
      <td class="number money-positive">${money(b.totalPaid)}</td>
      <td class="number">${money(b.totalWaived)}</td>
      <td class="number ${b.remainingBalance>0?'money-negative':''}"><strong>${money(b.remainingBalance)}</strong></td>
     </tr>`)
    )}
   </div>
   <div class="panel">
    <h2>2. كشف حركة المديونيات والخدمات التفصيلي (مرتب زمنياً بدقة — مدين / دائن / رصيد تراكمي)</h2>
    ${table(
     ['التاريخ','الفترة','نوع الحركة','البند / الصندوق','السجل التجاري','رقم المرجع / السند','البيان','تحمل الشركة','مدين (+ زيادة دين)','دائن (- سداد / إعفاء)','الرصيد التراكمي'],
     st.statementLines.map((l:any)=>`<tr>
      <td class="number">${esc(l.date)}</td>
      <td class="number">${esc(l.period)}</td>
      <td>${badge(l.typeAr,l.credit>0?'good':l.type==='company_borne_service'?'':'warn')}</td>
      <td>${esc(l.categoryAr)}</td>
      <td>${esc(l.sponsorNameAr)}</td>
      <td class="number">${esc(l.reference)}</td>
      <td>${esc(l.description)}</td>
      <td class="number">${l.companyBorneAmount>0?money(l.companyBorneAmount):'—'}</td>
      <td class="number ${l.debit>0?'money-negative':''}">${l.debit>0?money(l.debit):'—'}</td>
      <td class="number ${l.credit>0?'money-positive':''}">${l.credit>0?money(l.credit):'—'}</td>
      <td class="number"><strong>${money(l.runningBalance)}</strong></td>
     </tr>`)
    )}
   </div>
   <div class="split">
    <div class="panel">
     <h2>3. القيود المحاسبية المزدوجة المرتبطة بالمندوب (${(st.journalEntries||[]).length})</h2>
     ${table(
      ['رقم القيد','التاريخ','النوع','البيان','مدين','دائن',''],
      (st.journalEntries||[]).map((j:any)=>`<tr>
       <td class="number"><strong>${esc(j.journalNumber)}</strong></td>
       <td class="number">${esc(j.date)}</td>
       <td>${esc(j.transactionType)}</td>
       <td class="small">${esc(j.description)}</td>
       <td class="number">${money(j.totalDebit)}</td>
       <td class="number">${money(j.totalCredit)}</td>
       <td><button class="small secondary" data-enterprise="journal-detail" data-id="${esc(j.id)}">الأسطر</button></td>
      </tr>`)
     )}
    </div>
    <div class="panel">
     <h2>4. سجل مسيرات الرواتب والدفعات والاستقطاعات (${(st.payrollLedgerRows||[]).length})</h2>
     ${table(
      ['الفترة','نوع الحركة','المبلغ','المرجع','التاريخ'],
      (st.payrollLedgerRows||[]).map((r:any)=>`<tr>
       <td class="number">${esc(r.period)}</td>
       <td>${esc(r.kind==='approved_payroll'?'استحقاق مسير راتب معتمد':r.kind==='debt_installment'?'استقطاع قسط مديونية':r.kind==='payment'?'دفعة مصروفة للمندوب':r.kind)}</td>
       <td class="number ${r.amount<0?'money-negative':'money-positive'}"><strong>${money(r.amount)}</strong></td>
       <td class="number small">${esc(r.sourceRef)}</td>
       <td class="number small">${esc((r.createdAt||'').slice(0,10))}</td>
      </tr>`)
     )}
    </div>
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
      ['العنوان','من قسم','إلى قسم','الرتبة المطلوبة','الحالة','ملاحظة القرار',''],
      approvals.map((a:any)=>`<tr>
       <td><strong>${esc(a.titleAr)}</strong><div class="muted small">${esc(a.bodyAr)}</div></td>
       <td>${esc(a.fromDepartment)}</td>
       <td>${esc(a.targetDepartment)}</td>
       <td>رتبة ${esc(a.requiredRankLevel)}</td>
       <td>${badge(a.status==='approved'?'مصدق':a.status==='rejected'?'مرفوض':a.status==='suspended'?'معلق':'بانتظار التصديق',a.status==='approved'?'good':a.status==='rejected'?'bad':'warn')}</td>
       <td>${esc(a.decisionNote||'—')}</td>
       <td>${can('approvals.decide')&&(a.status==='pending'||a.status==='suspended')?`<button class="small good" data-enterprise="approval-decide" data-id="${esc(a.id)}">قرار وتصديق</button>`:esc(a.decidedBy||'')}</td>
      </tr>`)
     )}
    </div>
    <div class="panel">
     <div class="panel-title">
      <h2>مركز الإشعارات والمهام (To-Do) وتطبيق الجوال (Google Play / FCM)</h2>
      <div class="actions">
       <a class="button secondary" href="/rider-app" target="_blank">📱 فتح تطبيق المندوب (/rider-app)</a>
       ${userRole==='Admin'||userRole==='SuperAdmin'?`<button class="secondary" data-enterprise="firebase-config">⚙️ إعدادات ربط Firebase و Google Play</button>`:''}
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
      ${userRole==='Admin'||userRole==='SuperAdmin'?`<button class="secondary" data-enterprise="toggle-promissory-flag" data-enabled="${promissory.featureEnabled?'false':'true'}">${promissory.featureEnabled?'تعليق الموديول (Dormant Mode)':'تفعيل الموديول للاختبار'}</button>`:''}
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
  'شجرة حسابات هرمية، عزل مالي لصناديق جاهز عن السلف وطلبات HR، فصل كامل لذمم السجلات التجارية الثلاثة، واعتمادات متبادلة.',
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

function userDialog(id?:string){
 const u=cache.users?.find((x:any)=>x.id===id)||{};
 const rolesCat=cache.rolesCatalog||{roles:[],permissions:[],departments:[]};
 const roleEntries=(rolesCat.roles||[]).length
  ? rolesCat.roles.map((r:any)=>[r.code,`${r.nameAr} (${r.code})`])
  : Object.entries(roleLabels);
 const deptEntries=[
  ['FIN','الشؤون المالية والخزينة (FIN)'],
  ['HR','الموارد البشرية والعلاقات الحكومية (HR)'],
  ['OPS','تشغيل المنصات والتطبيقات (OPS)'],
  ['FLT','الحركة والأسطول والورشة (FLT)'],
  ['MGT','الإدارة العليا والامتثال القانوني (MGT)']
 ];
 const perms=rolesCat.permissions||[];
 const userCustom=new Set<string>(u.customPermissions||[]);

 modal(id?'تعديل دور وصلاحيات المستخدم':'إنشاء مستخدم وتعيين الدور والصلاحيات',`<form id="user-form" class="form-grid">
  ${id?'':fieldInput('اسم المستخدم','username','','text',true)+fieldInput('كلمة مرور12حرفًا على الأقل','password','','password',true)}
  <label>الدور الوظيفي المعتمد<select name="role" id="user-role-select">${options(roleEntries,u.role||'Reviewer')}</select></label>
  <label>القسم التابع له<select name="department" id="user-dept-select">${options(deptEntries,u.department||'OPS')}</select></label>
  <label>الرتبة الوظيفية (للاعتمادات المتبادلة)
   <select name="rankLevel" id="user-rank-select">
    ${options([['1','رتبة 1 — أخصائي / محاسب صندوق / مشرف'],['2','رتبة 2 — مسؤول علاقات / مشرف أول'],['3','رتبة 3 — رئيس حسابات / مدير قسم'],['4','رتبة 4 — مدير مالي / مدير تشغيل'],['5','رتبة 5 — مدير عام / إدارة عليا']],String(u.rankLevel||1))}
   </select>
  </label>
  ${id?`<label>حالة الحساب<select name="status">${options([['active','نشط'],['suspended','موقوف']],u.status)}</select></label>`:''}
  ${perms.length?`
   <details class="full" style="background:var(--raised);padding:10px 14px;border-radius:10px">
    <summary style="cursor:pointer;font-weight:700">🔐 تخصيص صلاحيات ذرية إضافية لهذا المستخدم (${perms.length} صلاحية متاحة)</summary>
    <div class="form-grid" style="margin-top:10px">
     ${perms.map((p:any)=>`<label class="check small"><input type="checkbox" data-custom-perm="${esc(p.code)}" ${userCustom.has(p.code)?'checked':''}><b>${esc(p.nameAr)}</b> <span class="muted">(${esc(p.moduleAr)})</span></label>`).join('')}
    </div>
   </details>
  `:''}
  ${perms.length?`<details class="full"><summary>منع صلاحيات افتراضية لهذا المستخدم (المنع له أولوية)</summary>${perms.map((p:any)=>`<label class="check small"><input type="checkbox" data-deny-perm="${esc(p.code)}" ${(u.deniedPermissions||[]).includes(p.code)?'checked':''}>منع ${esc(p.nameAr)}</label>`).join('')}<p>مدير النظام يظل مسؤولًا كامل الصلاحية؛ استخدم دورًا محدودًا لمن يحتاج الاستثناءات.</p></details>`:''}
  <details class="full"><summary>نطاق البيانات والحسابات المالية المسموحة</summary><p class="muted">الصلاحية تحدد الفعل؛ والنطاق يحدد الشركة والحساب والفئة والمنصة. الدور المحدود لا يرى أموال شركات أخرى. اختيار لا شيء يعني عدم إسناد نطاق، وليس السماح للجميع.</p>
  ${[['sponsorIds','السجلات القانونية',(rolesCat.financialScopeCatalog?.sponsors||[]).map((x:any)=>[x.id,x.nameAr])],['accountCodes','الصناديق والبنوك',(rolesCat.financialScopeCatalog?.accounts||[]).map((x:any)=>[x.code,x.nameAr])],['categoryCodes','أنواع المديونيات',(rolesCat.financialScopeCatalog?.categories||[]).map((x:any)=>[x.code,x.nameAr])],['platforms','المنصات',(rolesCat.financialScopeCatalog?.platforms||[]).map((x:string)=>[x,x])]].map(([key,label,entries]:any)=>`<fieldset><legend>${label}</legend><div class="form-grid">${entries.map(([value,text]:string[])=>`<label class="check small"><input type="checkbox" data-financial-scope="${key}" value="${esc(value)}" ${(u.financialScope?.[key]||[]).includes(value)?'checked':''}>${esc(text)}</label>`).join('')}</div></fieldset>`).join('')}
  </details>
  <button>حفظ بيانات وصلاحيات المستخدم</button>
 </form>`);

 const roleSel=$('#user-role-select');
 if(roleSel){
  roleSel.onchange=()=>{
   const def=(rolesCat.roles||[]).find((r:any)=>r.code===roleSel.value);
   if(def){
    if($('#user-dept-select'))$('#user-dept-select').value=def.defaultDepartment;
    if($('#user-rank-select'))$('#user-rank-select').value=String(def.defaultRankLevel);
   }
  };
 }

 $('#user-form').onsubmit=submit(async d=>{
  const customPermissions=[...document.querySelectorAll<HTMLInputElement>('#user-form [data-custom-perm]:checked')].map(el=>el.dataset.customPerm!);
  await api(id?`/users/${id}/status`:'/users',{...d,rankLevel:Number(d.rankLevel||1),customPermissions,financialScope:Object.fromEntries(['sponsorIds','accountCodes','categoryCodes','platforms'].map(key=>[key,[...document.querySelectorAll<HTMLInputElement>(`#user-form [data-financial-scope="${key}"]:checked`)].map(el=>el.value)])),deniedPermissions:[...document.querySelectorAll<HTMLInputElement>('#user-form [data-deny-perm]:checked')].map(el=>el.dataset.denyPerm!)},id?'PUT':'POST');
  $('#modal').close();
  await show('users');
 });
}

function debtDialog(id?:string){
 const d=cache.debts?.find((x:any)=>x.id===id)||{};
 const catalog=cache.financeCatalog||{categories:[],sponsors:[],vaults:[]};
 const sectors:Record<string,string>={HR:'hr',Advances:'advance',Fleet:'fleet',Platforms:'platform',Housing:'housing',Admin:'administrative'};
 const allowedCategories=(catalog.categories||[]).filter((c:any)=>c.code!=='NegativeSalaryCarryover'&&can(c.requiredPermission||'debts.create.'+(c.code==='EquipmentLoan'?'equipment':sectors[c.sector])));
 const catOptions=allowedCategories.length
  ? allowedCategories.map((c:any)=>[c.code,`${c.nameAr} (${c.sector}) — ح/${c.expenseAccountCode}`])
  : [];
 const spOptions=(catalog.sponsors||[]).map((s:any)=>[s.id,`${s.shortNameAr} — ${s.nameAr} (${s.unifiedNumber})`]);
 const initialCategory=allowedCategories[0]?.code||'WorkPermit';
 const initialSponsor=catalog.sponsors?.[0]?.id||'';
 const initialVaults=filterVaultsForCategory(catalog.vaults||[],initialCategory,'disbursement',initialSponsor);
 const vaultOptions=initialVaults.map((v:any)=>[v.code,`${v.code} — ${v.nameAr}`]);

 modal(id?'إعادة جدولة وتحديث حالة المديونية':'تسجيل مديونية / خدمة HR / سلفة جديدة',`<form id="debt-form" class="form-grid">
  ${!id?`
   <div class="notice full" id="debt-vault-rule-banner">
    <strong>تخصيص الحسابات حسب نوع العملية:</strong> يظهر في قائمة الصرف أدناه فقط الصندوق الداخلي الرئيسي وفروعه المخصصة للمديونيات وطلبات HR (<code>MAIN.*.DEBT</code>) وحسابات البنوك (<code>BANK.*</code>). يُحجب تلقائياً <b>صندوق جاهز (SUP.*)</b> و<b>حساب رسوم جاهز (*.FEE)</b> و<b>بترو آب (PETRO.*)</b> في طلبات الموارد البشرية والسلف لعدم صحة المعاملة مالياً.
   </div>
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
    <select name="initiatingDepartment" id="debt-init-dept">
     <option value="HR">الموارد البشرية والعلاقات الحكومية (HR)</option>
     <option value="FIN">الشؤون المالية والخزينة (FIN)</option>
     <option value="FLT">الحركة والأسطول والورشة (FLT)</option>
     <option value="OPS">تشغيل المنصات (OPS)</option>
    </select>
   </label>
   <label>السجل التجاري الدائن / جهة الصرف<select name="creditorSponsorId" id="debt-sponsor-select">${options(spOptions)}</select></label>
   <label>نوع الحدث المالي<select name="entryMode" id="debt-entry-mode"><option value="Accrual">إثبات خدمة/التزام غير نقدي (دون صرف)</option><option value="Disbursement">صرف فعلي مثبت من صندوق الشركة أو البنك</option></select></label>
   <label id="debt-platform-field">المنصة المحيلة للدين<select name="platform"><option value="">اختر المنصة عند إحالة دين منها</option>${options(['Jahez','Keeta','HungerStation','Ninja','Other'].map(x=>[x,x]))}</select></label>
   <label id="debt-recognition-field">مقابل الإثبات غير النقدي<select name="recognitionAccountCode"><option value="2130">التزام فاتورة/خدمة غير مسددة — 2130</option><option value="4103">استحقاق خدمة للشركة — 4103</option><option value="5301">تسوية تكلفة راتب معتمدة — 5301</option></select><span class="muted">الدين المحال من منصة يثبت مقابل حساب المنصة، ولا يصرف من عهدتها.</span></label>
   <label id="debt-cash-field">الصندوق أو البنك الدافع للتكلفة<select name="paymentVaultCode" id="debt-vault-select">${options(vaultOptions)}</select></label>
   ${fieldInput('إجمالي مبلغ الذمة على المندوب (أو التكلفة)','totalAmount','','number',true)}
   <div id="company-share-wrap" class="hide">${fieldInput('مبلغ تحمل الشركة (يُقيد في حساب المصروف)','companyShareAmount','0','number')}</div>
   ${fieldInput('القسط الشهري المستقطع من الراتب','monthlyInstallment','','number',true)}
   ${fieldInput('مرجع المستند / السند / قرار الصرف','sourceRef',`HR-${Date.now().toString().slice(-6)}`,'text',true)}
   ${fieldInput('بداية سريان الاستقطاع','effectiveFrom',month,'month',true)}
   <label class="check full"><input name="requireMutualApproval" type="checkbox">يتطلب تصديق واعتماد القسم المقابل (المالية ⇄ HR) قبل تفعيل القيد</label>
  `:`
   <label>الحالة<select name="status">${options([['active','نشط'],['paused','إيقاف مؤقت']],d.status)}</select></label>
   ${fieldInput('القسط الشهري','monthlyInstallment',d.monthlyInstallment||'','number',true)}
   ${(userRole==='Admin'||userRole==='SuperAdmin')&&!d.creditorSponsorId?`<label>ربط السجل الدائن بعد المراجعة<select name="creditorSponsorId"><option value="">اترك غير مربوط</option>${options(spOptions)}</select></label>`:''}
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
 const spSelect=$('#debt-sponsor-select');
 const vaultSelect=$('#debt-vault-select');
 const refreshAllowedVaults=()=>{
  if(!catSelect||!vaultSelect)return;
  const catCode=catSelect.value;
  const spId=spSelect?.value||'';
  const validVaults=filterVaultsForCategory(catalog.vaults||[],catCode,'disbursement',spId);
  vaultSelect.innerHTML=options(validVaults.map((v:any)=>[v.code,`${v.code} — ${v.nameAr}`]));
  const found=(catalog.categories||[]).find((c:any)=>c.code===catCode);
  const totalInput=$('#debt-form input[name=totalAmount]');
  const instInput=$('#debt-form input[name=monthlyInstallment]');
  if(found&&found.defaultReferenceCost>0&&totalInput&&!totalInput.value){
   totalInput.value=String(found.defaultReferenceCost);
   if(instInput&&!instInput.value)instInput.value=String(Math.min(found.defaultReferenceCost,1000));
  }
 };
 const entrySelect=$('#debt-entry-mode');
 const syncEntry=()=>{if(!entrySelect||!catSelect)return;const cash=entrySelect.value==='Disbursement',platform=catSelect.value==='PlatformTransferredDebt';$('#debt-cash-field')?.classList.toggle('hide',!cash);if(vaultSelect)vaultSelect.disabled=!cash;$('#debt-recognition-field')?.classList.toggle('hide',cash||platform);const recognition=$('#debt-form [name=recognitionAccountCode]');if(recognition)recognition.disabled=cash||platform;$('#debt-platform-field')?.classList.toggle('hide',!platform);const platformSelect=$('#debt-form [name=platform]');if(platformSelect){platformSelect.disabled=!platform;platformSelect.required=platform;}};
 if(entrySelect)entrySelect.onchange=syncEntry;
 if(catSelect)catSelect.onchange=()=>{refreshAllowedVaults();if(entrySelect)entrySelect.value=['Advance','CashAdvance','BankAdvance','CashCustody'].includes(catSelect.value)?'Disbursement':'Accrual';syncEntry();};
 syncEntry();
 if(spSelect)spSelect.onchange=refreshAllowedVaults;

 $('#debt-form').onsubmit=submit(async(v,form)=>{
  await api(id?`/debts/${id}`:'/debts',{expectedRemainingBalance:d.remainingBalance,
   ...v,
   originSponsorId:v.creditorSponsorId,
   requireMutualApproval:form.elements.requireMutualApproval?.checked||false,
   agreementReviewed:form.elements.agreementReviewed.checked
  },id?'PUT':'POST');
  $('#modal').close();
  await show('debts');
  toast(id?'تم تحديث جدولة المديونية':'تم حفظ الحدث المالي حسب نوعه وحالة اعتماده المزدوج بنجاح');
 });
}

function debtCollectDialog(id:string){
 const d=cache.debts?.find((x:any)=>x.id===id);
 if(!d)return;
 const catalog=cache.financeCatalog||{vaults:[]};
 const validVaults=filterVaultsForCategory(catalog.vaults||[],d.category||'Advance','collection',d.creditorSponsorId,d.platform);
 const vaultOptions=validVaults.map((v:any)=>[v.code,`${v.code} — ${v.nameAr}`]);

 modal(`تحصيل وتسوية مديونية — ${d.riderName||d.iqama}`,`<form id="debt-collect-form" class="form-grid">
  <div class="notice full">
   البند: <strong>${esc(d.categoryAr||d.category)}</strong> · السجل الدائن: <strong>${esc(d.creditorSponsorNameAr||'غير مربوط — راجع الجدولة')}</strong> · الرصيد المتبقي حالياً: <strong class="number">${money(d.remainingBalance)} ر.س</strong>
   <br><small>تظهر أدناه فقط الصناديق والحسابات المصرح لها باستلام هذا النوع من المديونية (الصندوق الداخلي الرئيسي وفروعه والبنوك)، مع تسوية آلية في <b>جاري السجلات الشقيقة (1310)</b> إذا تم التحصيل في صندوق سجل آخر.</small>
  </div>
  ${fieldInput('المبلغ المحصل فعلياً (كاش أو بنك)','collectedAmount',String(d.monthlyInstallment||d.remainingBalance),'number',true)}
  ${fieldInput('مبلغ الإعفاء / التنازل الإداري (ح/ 5202)','waivedAmount','0','number')}
  <label>الصندوق أو البنك المستلم للمبلغ (مفلتر حسب نوع المديونية)<select name="targetVaultCode">${options(vaultOptions,d.paymentVaultCode||validVaults[0]?.code)}</select></label>
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
  const res=await api(`/debts/${id}/transfer-sponsor`,{...v,expectedRemainingBalance:d.remainingBalance,expectedCreditorSponsorId:d.creditorSponsorId});
  $('#modal').close();
  await show('debts');
  toast(`تم نقل ملكية المديونية وإثبات القيد ${res.journal.journalNumber}`);
 });
}

function bulkCollectDialog(){
 const validVaults=filterVaultsForCategory(cache.financeCatalog?.vaults||[],'Advance','collection');
 const vaultOptions=validVaults.map((v:any)=>[v.code,`${v.code} — ${v.nameAr}`]);
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
 const allVaults=(cache.financeCatalog?.vaults||[]).filter((v:any)=>['MainInternal','Bank'].includes(v.vaultKind)&&!['JahezFee','GeneralAndSettlement','JahezSettlement'].includes(v.vaultPurpose));
 const fromOptions=allVaults.map((v:any)=>[v.code,`${v.code} — ${v.nameAr}`]);
 const toOptions=allVaults.filter((v:any)=>!(v.vaultKind==='JahezCashbox'&&v.vaultPurpose==='JahezFee')).map((v:any)=>[v.code,`${v.code} — ${v.nameAr}`]);

 modal('تحويل داخلي بين صندوق الشركة والبنك',`<form id="vault-transfer-form" class="form-grid">
  <div class="notice full">
   <strong>الضوابط المالية للحركة بين الصناديق:</strong><br>
   • التحويل العام بين حسابين مختلفين داخل السجل نفسه، من الرصيد الفعلي المتاح.<br>
   • عهد جاهز والتصفيات والرسوم لها مسار التسليم المخصص، وتظل منفصلة عن تمويل السلف وHR.<br>
   • بترو آب حساب مقاصة وقود، وليس صندوق نقدية للتحويلات.
  </div>
  <label>من صندوق / بنك (طرف دائن ينقص)<select name="fromVaultCode" id="vtr-from" required>${options(fromOptions,'MAIN.BW.DEBT')}</select></label>
  <label>إلى صندوق / بنك (طرف مدين يزيد)<select name="toVaultCode" id="vtr-to" required>${options(toOptions,'BANK.BW')}</select></label>
  ${fieldInput('المبلغ المحول (ر.س)','amount','','number',true)}
  ${fieldInput('رقم مرجع التحويل / الإيداع','sourceRef',`VTR-${Date.now().toString().slice(-6)}`,'text',true)}
  <label class="full">البيان والتفاصيل<input name="description" value="تحويل نقدية الشركة داخل السجل نفسه"></label>
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

function jahezFeeDialog(){
 const spOptions=(cache.financeCatalog?.sponsors||[]).map((s:any)=>[s.code,`${s.shortNameAr} — ${s.nameAr}`]);
 const feeVaults=(cache.financeCatalog?.vaults||[]).filter((v:any)=>v.vaultPurpose==='JahezFee'||v.vaultPurpose==='Combined').map((v:any)=>[v.code,`${v.code} — ${v.nameAr}`]);
 modal('قبض رسوم فتح وتفعيل حساب جاهز جديد (200 ر.س)',`<form id="jahez-fee-form" class="form-grid">
  <div class="notice full">
   رسوم فتح حساب جاهز إيراد للشركة (ح/4101) حسب القرار المعتمد. تُحصّل في عهدة المشرف للرسوم (<code>SUP.*.FEE</code>) أو حساب الرسوم الداخلي (<code>MAIN.*.FEE</code>، ومنها <code>MAIN.TR.FEE</code>)، وتظل منفصلة عن تصفيات جاهز والسلف وطلبات HR.
  </div>
  <label>السجل التجاري للحساب<select name="sponsorId">${options(spOptions,'BW')}</select></label>
  <label>حساب / عهدة الرسوم المستلمة (قبض فقط)<select name="targetVaultCode">${options(feeVaults,'SUP.BW.FEE')}</select></label>
  <input type="hidden" name="beneficiary" value="company">
  <label>المندوب (اختياري)<select name="iqama"><option value="">بدون تحديد مندوب</option>${riderOptions()}</select></label>
  ${fieldInput('مبلغ رسوم فتح الحساب (ر.س)','amount','200','number',true)}
  ${fieldInput('رقم سند قبض الرسوم','sourceRef',`JFEE-${Date.now().toString().slice(-6)}`,'text',true)}
  <label class="full">ملاحظات البيان<input name="description" value="قبض رسوم فتح وتفعيل حساب جاهز جديد (200 ر.س)"></label>
  <button class="good">تسجيل قبض الرسوم وتوليد القيد المحاسبي</button>
 </form>`);
 $('#jahez-fee-form').onsubmit=submit(async v=>{
  const j=await api('/finance/jahez-fee',v);
  $('#modal').close();
  await show('debts');
  toast(`تم قيد رسوم فتح حساب جاهز بموجب القيد ${j.journalNumber}`);
 });
}

function jahezHandoverDialog(){
 const spOptions=(cache.financeCatalog?.sponsors||[]).map((s:any)=>[s.code,`${s.shortNameAr} — ${s.nameAr}`]);
 const accounts=cache.accounts||[];
 const supAccounts=accounts.filter((a:any)=>a.code?.startsWith('SUP.'));
 modal('تسليم وتصفير عهدة صندوق جاهز اليومية إلى الصندوق الداخلي الرئيسي (Zero-Out Handover)',`
  <div class="notice">
   يسجل هذا الإجراء طلب تسليم عهدة مشرف جاهز (<code>SUP.*.SET</code> و <code>SUP.*.FEE</code>) ولا ينقل نقدًا حتى تستلمه المالية وتطابق العد لكل غرض:
   <br>• سجل البوابا وإكسبرس: ترحل التصفيات إلى <code>MAIN.*.SET</code> والرسوم إلى <code>MAIN.*.FEE</code>.
   <br>• التجارية: تبقى التصفيات في <code>MAIN.TR.SET</code> والرسوم في <code>MAIN.TR.FEE</code> دون دمج.
  </div>
  ${table(
   ['رمز خزنة جاهز','اسم الحساب','السجل التجاري','الرصيد الحالي القابل للتسليم'],
   supAccounts.map((a:any)=>`<tr>
    <td class="number"><strong>${esc(a.code)}</strong></td>
    <td>${esc(a.nameAr)}</td>
    <td>${esc(a.defaultSponsorNameAr)}</td>
    <td class="number ${a.netBalance>0?'money-positive':''}"><strong>${money(a.netBalance)} ر.س</strong></td>
   </tr>`)
  )}
  <form id="jahez-handover-form" class="form-grid" style="margin-top:12px">
   <label>اختر السجل التجاري لتصفير عهدة جاهز الخاصة به<select name="sponsorId">${options(spOptions,'BW')}</select></label>
   ${fieldInput('مبلغ تصفية الكاش المسلم (اتركه فارغاً لتصفير كامل الرصيد)','settlementAmount','','number')}
   ${fieldInput('مبلغ رسوم الحسابات المسلم (اتركه فارغاً لتصفير كامل الرصيد)','feeAmount','','number')}
   ${fieldInput('رقم سند التسليم والتصفير','sourceRef',`HND-${Date.now().toString().slice(-6)}`,'text',true)}
   <button class="good full">إرسال طلب تسليم للمالية دون نقل النقدية الآن</button>
  </form>
 `);
 $('#jahez-handover-form').onsubmit=submit(async v=>{
  const res=await api('/finance/jahez-handover',v);
  $('#modal').close();
  await show('debts');
  toast(`سُجل طلب التسليم ${res.sourceRef}؛ النقد باقٍ بالعهدة حتى استلام المالية`);
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
    <div><span class="muted small">الصندوق / الحساب الصارف:</span><br><strong class="number">${esc(d?.paymentVaultCode||'MAIN.BW.DEBT')}</strong></div>
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
  ${userRole==='Admin'||userRole==='SuperAdmin'?'<label class="full check"><input id="approval-override-self" type="checkbox">تجاوز إداري صريح عند مراجعة طلبك (تلزم ملاحظة توضيحية)</label>':''}
  <button class="good">حفظ القرار وإشعار القسم المرسل</button>
 </form>`);
 $('#approval-decide-form').onsubmit=submit(async v=>{
  await api(`/approvals/${id}/decide`,{...v,overrideSelf:$('#approval-override-self')?.checked||false});
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
  ${userRole==='Admin'||userRole==='SuperAdmin'?`
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
 case'jahez-fee':jahezFeeDialog();break;
 case'jahez-handover':jahezHandoverDialog();break;
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
 case'legacy-cash-allocation':legacyCashAllocationDialog();break;case'jahez-receive':jahezReceiveDialog(id!);break;case'treasury-opening':treasuryOpeningDialog();break;case'debt-reconcile':debtOpeningDialog(id!);break;case'supplier-pay':supplierPaymentDialog(id!);break;case'jahez-transfer':jahezTransactionDialog('transfer');break;case'jahez-platform-settle':jahezTransactionDialog('settle');break;case'jahez-fee-deposit':jahezTransactionDialog('fee');break;case'campaign-delete':await formDeleteDialog(id!,false);break;case'builder-delete':await formDeleteDialog(id!,true);break;case'campaign-edit':campaignEditDialog(id!);break;case'analytics-review':await show('responses');responseReview(id);break;case'copy':await navigator.clipboard.writeText(b.dataset.url!);toast('تم نسخ الرابط');break;case'qr':modal('رمز المشاركة QR',`<img class="qr-image" alt="QR لمشاركة النموذج" src="${esc(b.dataset.url)}">`);break;case'user-new':userDialog();break;case'user-edit':userDialog(id);break;case'debt-new':debtDialog();break;case'debt-edit':debtDialog(id);break;case'baseline':await baselineDialog();break;case'builder-new':builderDialog();break;case'builder-edit':builderDialog(id);break;case'builder-publish':if(confirm('نشر النموذج وتثبيت الأسئلة؟')){await api(`/forms/builder/${id}/publish`,{});await show('builder');}break;case'builder-close':if(confirm('إغلاق استقبال الردود؟')){await api(`/forms/builder/${id}/close`,{});await show('builder');}break;case'analytics':await analyticsDialog(id!);break;case'inquiry-code':await inquiryCodeDialog();break;
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

async function treasuryOpeningDialog(){
 modal('مطابقة الأرصدة الافتتاحية الموثقة',`<div class="notice warn">تُضاف تسوية موثقة دون تعديل القيود القديمة. أدخل الرصيد الطبيعي الفعلي بعد مطابقته مع الصندوق أو كشف البنك. افصل رصيد البنك الحر عن المخصص لجاهز حتى لا تُحسب الأموال مرتين. أرصدة الرواتب يجب أن تطابق الدفتر القديم.</div><a class="button secondary" href="/api/finance/opening/template">تنزيل قالب Excel v1</a><form id="treasury-opening-form"><label>ملف الأرصدة<input type="file" id="treasury-opening-file" accept=".xlsx,.csv" required></label><button>معاينة وفحص الأثر</button></form><div id="treasury-opening-preview"></div>`);
 $('#treasury-opening-form').onsubmit=async e=>{e.preventDefault();try{const file=$('#treasury-opening-file').files[0];const dataBase64=await financeFileBase64(file);const p=await api('/finance/opening/preview',{filename:file.name,dataBase64});$('#treasury-opening-preview').innerHTML=table(['الحساب','السجل','الإقامة','الرصيد الحالي','الرصيد المراجع','فرق التسوية','المستند','البيان'],p.rows.map((r:any)=>`<tr><td>${esc(r.accountCode)}</td><td>${esc(r.sponsorCode)}</td><td>${esc(r.iqama)}</td><td class="number">${money(r.currentBalance)}</td><td class="number">${money(r.verifiedBalance)}</td><td class="number">${money(r.adjustment)}</td><td>${esc(r.sourceRef)}</td><td>${esc(r.notes)}</td></tr>`))+`<button id="treasury-opening-commit" class="good">راجعت المستندات وأعتمد تسوية الأرصدة المعروضة</button>`;$('#treasury-opening-commit').onclick=async e=>{const b=e.currentTarget as HTMLButtonElement;b.disabled=true;try{await api('/finance/opening/commit',{rows:p.rows,fingerprint:p.fingerprint,confirm:true});$('#modal').close();await show('debts');toast('تم حفظ التسوية الافتتاحية وأثرها دون تعديل التاريخ')}catch(err:any){toast(err.message,true);b.disabled=false;}};}catch(err:any){toast(err.message,true);}};
}
function financeFileBase64(file:File):Promise<string>{if(!file||file.size>8*1024*1024)throw Error('اختر ملفًا بحد أقصى 8 ميجابايت');return new Promise((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(String(reader.result).split(',')[1]);reader.onerror=reject;reader.readAsDataURL(file);});}
function debtOpeningDialog(id:string){
 const d=cache.debts?.find((x:any)=>x.id===id);if(!d)return;const sponsors=cache.financeCatalog?.sponsors||[];
 modal('مطابقة افتتاح الدين التاريخي',`<div class="notice warn">المتبقي المحفوظ: ${money(d.remainingBalance)} ر.س. لا يُعدل الأصل أو السداد التاريخي؛ تضاف تسوية بالقيمة اللازمة لمطابقة الذمة الفعلية فقط.</div><form id="debt-opening-form" class="form-grid"><label>السجل صاحب الدين<select name="sponsorId" required><option value="">حدد المالك من المستند</option>${options(sponsors.map((s:any)=>[s.id,s.nameAr]),d.creditorSponsorId)}</select></label>${fieldInput('مرجع مستند المطابقة','sourceRef','','text',true)}<label class="full">بيان ودليل الرصيد<textarea name="notes" required></textarea></label><button>معاينة المطابقة</button></form><div id="debt-opening-preview"></div>`);
 $('#debt-opening-form').onsubmit=submit(async v=>{const p=await api(`/debts/${id}/reconcile-opening/preview`,{...v,expectedRemainingBalance:d.remainingBalance,expectedCreditorSponsorId:d.creditorSponsorId});const r=p.rows[0];$('#debt-opening-preview').innerHTML=`<div class="notice">رصيد الذمة الحالي ${money(r.currentJournalBalance)} · المتبقي المراجع ${money(r.expectedRemainingBalance)} · أثر التسوية ${money(r.adjustment)} ر.س</div><button id="debt-opening-commit" class="good">اعتماد التسوية المعروضة</button>`;$('#debt-opening-commit').onclick=async e=>{const b=e.currentTarget as HTMLButtonElement;b.disabled=true;try{await api(`/debts/${id}/reconcile-opening/commit`,{rows:p.rows,fingerprint:p.fingerprint,confirm:true});$('#modal').close();await show('debts');toast('حُفظ افتتاح الدين المراجع')}catch(err:any){toast(err.message,true);b.disabled=false;}};});
}
function supplierPaymentDialog(id:string){
 const d=cache.debts?.find((x:any)=>x.id===id);if(!d)return;const vaults=filterVaultsForCategory(cache.financeCatalog?.vaults||[],d.category,'disbursement');
 modal('سداد التزام خدمة معتمد',`<div class="notice">هذا السداد يخفض التزام المورد 2130 ويخرج النقد مرة واحدة؛ لا ينشئ مديونية أخرى على المندوب.</div><form id="supplier-payment-form" class="form-grid"><label>الحساب الدافع<select name="paymentVaultCode" required>${options(vaults.map((v:any)=>[v.code,v.nameAr]))}</select></label>${fieldInput('مبلغ السداد','amount','','number',true)}${fieldInput('مرجع الدفع الفعلي','sourceRef','','text',true)}<label class="check full"><input type="checkbox" name="onBehalf" id="company-pay-on-behalf">دفع من بنك شركة أخرى بالنيابة مع إثبات الجاري</label><label class="full">بيان ومستند الدفع بالنيابة<textarea name="onBehalfNote"></textarea></label><button>تسجيل السداد وفحص الرصيد</button></form>`);
 $('#supplier-payment-form [name=amount]').step='.01';$('#supplier-payment-form').onsubmit=submit(async v=>{await api('/finance/liability-payment',{...v,debtId:id,onBehalf:!!$('#company-pay-on-behalf')?.checked});$('#modal').close();await show('debts');toast('حُفظ الدفع دون تكرار الدين')});
}
function jahezTransactionDialog(kind:'transfer'|'settle'|'fee'){
 const sponsors=cache.financeCatalog?.sponsors||[];const vaults=(cache.financeCatalog?.vaults||[]).filter((v:any)=>['GeneralAndSettlement','JahezSettlement'].includes(v.vaultPurpose));
 const title=kind==='transfer'?'تحويل عهدة جاهز بين الحسابات المخصصة':kind==='fee'?'إيداع حصيلة رسوم الشركة في البنك':'تسوية مع شركة جاهز';
 modal(title,`<div class="notice">تصفيات جاهز منفصلة عن HR والسلف. تسليم العهدة أو إيداع حصيلة الرسوم لا يولد إيرادًا ثانيًا. تسوية شركة جاهز من حساب البنك المخصص وبحد المطالبة/الالتزام المثبت.</div><form id="jahez-transaction-form" class="form-grid">${kind==='transfer'?`<label>من حساب<select name="fromVaultCode" required>${options(vaults.filter((v:any)=>v.vaultKind!=='JahezCashbox').map((v:any)=>[v.code,v.nameAr]))}</select></label><label>إلى حساب<select name="toVaultCode" required>${options(vaults.map((v:any)=>[v.code,v.nameAr]))}</select></label>`:`<label>السجل القانوني<select name="sponsorId" required><option value="">حدد السجل</option>${options(sponsors.map((s:any)=>[s.id,s.nameAr]))}</select></label>`}${kind==='settle'?'<label>نوع التسوية<select name="action"><option value="pay">سداد التزام مثبت لجاهز</option><option value="receive">استلام مستحق مثبت من جاهز</option></select></label>':''}${fieldInput('المبلغ الفعلي','amount','','number',true)}${fieldInput('مرجع التحويل/التسوية الفعلي','sourceRef','','text',true)}<label class="full">بيان المستند<textarea name="notes" required></textarea></label><button>تسجيل المعاملة الموثقة</button></form>`);
 $('#jahez-transaction-form [name=amount]').step='.01';$('#jahez-transaction-form').onsubmit=submit(async v=>{await api(kind==='transfer'?'/finance/jahez-transfer':kind==='fee'?'/finance/jahez-fee-deposit':'/finance/jahez-platform-settlement',v);$('#modal').close();await show('debts');toast('تمت المعاملة ضمن الحسابات المخصصة')});
}
async function approvePayrollDialog(iqama:string){
 const preview=cache.payroll.find((r:any)=>r.iqama===iqama);const rider=cache.riders.find((r:any)=>r.iqama===iqama);const catalog=await api('/finance/catalog');
 modal('اعتماد مسير وترحيله محاسبيًا',`<div class="notice">${esc(rider?.name||iqama)} · ${esc(month)} · صافي الرصيد ${money(preview.closingBalance)} ر.س. يُثبت الاستحقاق والخصومات دون صرف نقد؛ الدفعة لها مستند وحساب مستقلان.</div><form id="payroll-accounting-form"><label>السجل المالي صاحب المسير<select name="sponsorId" required><option value="">حدد السجل من العقد</option>${options(catalog.sponsors.map((s:any)=>[s.id,s.nameAr]),rider?.financeSponsorId||rider?.sponsorId||'')}</select></label><button class="good">اعتماد المسير والقيود مرة واحدة</button></form>`);
 $('#payroll-accounting-form').onsubmit=submit(async v=>{await api('/payroll/approve',{...v,iqama,period:month,fingerprint:preview.fingerprint});$('#modal').close();await show('payroll');toast('اعتمد المسير وقيده المحاسبي')});
}
async function accountingPaymentDialog(){
 const catalog=await api('/finance/catalog');const vaults=catalog.vaults.filter((v:any)=>['DebtAndHr','Bank'].includes(v.vaultPurpose));
 modal('تسجيل صرف راتب فعلي',`<form id="accounting-payment-form" class="form-grid"><label>المندوب<select name="iqama" required>${riderOptions()}</select></label><label>السجل صاحب الراتب<select name="sponsorId" required><option value="">اختر السجل المالي</option>${options(catalog.sponsors.map((s:any)=>[s.id,s.nameAr]))}</select></label><label>حساب الشركة الدافع<select name="paymentVaultCode" required>${options(vaults.map((v:any)=>[v.code,v.nameAr]))}</select></label>${fieldInput('الفترة','period',month,'month',true)}${fieldInput('المبلغ المدفوع','amount','','number',true)}${fieldInput('مرجع التحويل/الدفع الفعلي','sourceRef','','text',true)}<label class="check full"><input type="checkbox" name="onBehalf" id="company-pay-on-behalf">دفع من بنك شركة أخرى بالنيابة مع إثبات الجاري</label><label class="full">بيان ومستند الدفع بالنيابة<textarea name="onBehalfNote"></textarea></label><button>تسجيل الدفع ضمن الرصيدين النقدي والمستحق</button></form>`);$('#accounting-payment-form [name=amount]').step='.01';$('#accounting-payment-form').onsubmit=submit(async v=>{await api('/payments',{...v,onBehalf:!!$('#company-pay-on-behalf')?.checked});$('#modal').close();await show('payroll');toast('حفظت الدفعة وقيدها دون تجاوز الرصيد')});
}

function legacyCashAllocationDialog(){
 modal('توزيع ومطابقة خزنة التجارية التاريخية مرة واحدة',`<div class="notice warn">أدخل الرصيد الفعلي النهائي لكل غرض، شاملاً ما سُجل بالفعل في الأبناء. تُغلق حركة الحساب القديم دون حذفها أو تكرارها؛ أي فرق عن الدفاتر يحتاج مستندًا وتصنيفًا صريحًا.</div><form id="legacy-cash-form" class="form-grid">${fieldInput('الرصيد الفعلي للمديونيات وHR','debtBalance','','number',true)}${fieldInput('الرصيد الفعلي لتصفيات جاهز','settlementBalance','','number',true)}${fieldInput('الرصيد الفعلي لرسوم الشركة','feeBalance','','number',true)}<label>مقابل فرق المطابقة إن وجد<select name="differenceAccountCode"><option value="">لا فرق متوقع / حدد التصنيف إذا وجد</option><option value="3101">افتتاح مفقود موثق — 3101</option><option value="5204">عجز نقدي مراجع — 5204</option><option value="4104">زيادة نقدية موثقة — 4104</option></select></label>${fieldInput('مرجع مستند الفرز والعد','sourceRef','','text',true)}<label class="full">بيان المراجعة ومصدر كل رصيد<textarea name="notes" required></textarea></label><button>معاينة الأثر قبل الاعتماد</button></form><div id="legacy-cash-preview"></div>`);
 document.querySelectorAll<HTMLInputElement>('#legacy-cash-form input[type=number]').forEach(el=>el.step='.01');
 $('#legacy-cash-form').onsubmit=submit(async v=>{const p=await api('/finance/legacy-cash-allocation/preview',v),r=p.rows[0];$('#legacy-cash-preview').innerHTML=`<div class="notice">الرصيد المباشر القديم ${money(r.bookBalance)} · الأبناء المسجلون ${money(r.existingChildrenTotal)} · الإجمالي الدفتري ${money(r.currentTotal)} · الإجمالي الفعلي ${money(r.verifiedTotal)} · فرق المطابقة ${money(r.reconciliationDifference)} ر.س · المقابل ${esc(r.differenceAccountCode||'لا فرق')}</div>${table(['الحساب','الرصيد الحالي','الرصيد الفعلي النهائي'],[['DEBT','debtBalance'],['SET','settlementBalance'],['FEE','feeBalance']].map(([code,key])=>`<tr><td>MAIN.TR.${code}</td><td class="number">${money(r['current_'+key])}</td><td class="number">${money(r[key])}</td></tr>`))}<button id="legacy-cash-commit" class="good">راجعت الفرز والمستند وأعتمد التسوية المعروضة</button>`;$('#legacy-cash-commit').onclick=async e=>{const b=e.currentTarget as HTMLButtonElement;b.disabled=true;try{await api('/finance/legacy-cash-allocation/commit',{rows:p.rows,fingerprint:p.fingerprint,confirm:true});$('#modal').close();await show('debts');toast('وزع الرصيد القديم دون تكراره أو حذف تاريخه')}catch(err:any){toast(err.message,true);b.disabled=false;}};});
}
function jahezReceiveDialog(id:string){
 const h=(cache.handovers||[]).find((x:any)=>x.id===id);if(!h)return;const self=h.submittedBy===user,admin=userRole==='Admin'||userRole==='SuperAdmin';
 modal('استلام عهدة جاهز بعد العد الفعلي',`<div class="notice">${esc(h.sourceRef)} — ${esc(h.sponsorNameAr)}<br>التصفيات المنتظرة ${money(h.settlementAmount)} · رسوم الشركة المنتظرة ${money(h.feeAmount)} ر.س. لا تُستبدل حصة غرض بحصة الآخر؛ العد لكل حساب مستقل. ${self?'يلزم مستلم مستقل عن مقدم الطلب؛ استثناء المدير فقط يحتاج توثيق السبب.':''}</div><form id="jahez-receive-form" class="form-grid"><label>قرار الاستلام<select name="decision" id="jahez-receive-decision"><option value="approved">مطابق — استلام وترحيل</option><option value="rejected">غير مطابق — رفض دون نقل أموال</option></select></label><div id="jahez-receive-counts" class="form-grid full">${fieldInput('عد التصفيات الفعلي (أدخل الصفر أيضًا)','settlementCounted','','number',true)}${fieldInput('عد رسوم الشركة الفعلي (أدخل الصفر أيضًا)','feeCounted','','number',true)}</div><label class="full">بيان الاستلام / سبب الرفض<textarea name="note" id="jahez-receive-note"></textarea></label>${self&&admin?'<label class="check full"><input id="jahez-receive-override" type="checkbox">تجاوز إداري صريح لاستلام طلبك مع بيان السبب</label>':''}<button class="good">حفظ القرار والعد الفعلي</button></form>`);
 document.querySelectorAll<HTMLInputElement>('#jahez-receive-form input[type=number]').forEach(el=>el.step='.01');const sync=()=>{const reject=$('#jahez-receive-decision').value==='rejected';$('#jahez-receive-counts').classList.toggle('hide',reject);document.querySelectorAll<HTMLInputElement>('#jahez-receive-counts input').forEach(el=>{el.required=!reject;el.disabled=reject;});$('#jahez-receive-note').required=reject||!!$('#jahez-receive-override')?.checked;};$('#jahez-receive-decision').onchange=sync;if($('#jahez-receive-override'))$('#jahez-receive-override').onchange=sync;
 $('#jahez-receive-form').onsubmit=submit(async v=>{await api(`/finance/jahez-handovers/${id}/${v.decision==='rejected'?'reject':'confirm'}`,{...v,overrideSelf:!!$('#jahez-receive-override')?.checked});$('#modal').close();await show('debts');toast(v.decision==='rejected'?'رفض التسليم وبقي النقد بالعهدة':'استلمت العهدة ورحلت مرة واحدة')});
}
