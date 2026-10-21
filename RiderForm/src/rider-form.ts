interface CampaignView { title:string; expiresAt:string; schema:string }
interface ChoicePayload { iqama:string; name:string; workSystem:'tiers'|'commission' }
interface ChoiceReceipt { receipt:string; message:string }
const container = document.querySelector<HTMLElement>('#public-content')!;
const token = location.pathname.split('/').pop()!;
const escapeText = (value:string):string => value.replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]!));
async function formRequest<T>(payload?:ChoicePayload):Promise<T> {
    const res = await fetch('/api/public/forms/'+encodeURIComponent(token),payload ? {method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)} : {});
    const data = await res.json() as T & {error?:string};
    if(!res.ok) throw new Error(data.error || (res.status===429?'حاول مرة أخرى بعد قليل.':'تعذر فتح النموذج أو إرسال الاختيار.'));
    return data;
}
async function openForm():Promise<void> {
    try {
        const campaign = await formRequest<CampaignView>();
        container.innerHTML = `<h2>${escapeText(campaign.title)}</h2><form id="rider-choice" class="form-grid">
        <label class="full">رقم الإقامة<input name="iqama" required inputmode="numeric" pattern="[0-9]{10}" minlength="10" maxlength="10" dir="ltr" autocomplete="off" aria-describedby="iqama-help"><span class="field-note" id="iqama-help">10 أرقام إنجليزية فقط (0–9)</span></label>
        <label class="full">الاسم الكامل<input name="name" required minlength="3" maxlength="150" autocomplete="name" aria-describedby="name-help"><span class="field-note" id="name-help">الاسم من جزأين على الأقل، بالعربية أو الإنجليزية</span></label>
        <fieldset class="full choice-options"><legend>اختيار نظام العمل</legend>
        <label class="check"><input type="radio" name="workSystem" value="tiers" required>نظام الراتب الجديد</label>
        <label class="check"><input type="radio" name="workSystem" value="commission" required>نظام العمولة السابق</label></fieldset>
        <div class="full" id="form-error" role="alert"></div><button class="full" type="submit">تسجيل اختياري</button></form>`;
        const form = document.querySelector<HTMLFormElement>('#rider-choice')!;
        const iqama = form.elements.namedItem('iqama') as HTMLInputElement;
        const name = form.elements.namedItem('name') as HTMLInputElement;
        iqama.addEventListener('input',()=>iqama.setCustomValidity(/^[0-9]{10}$/.test(iqama.value)?'':'اكتب 10 أرقام إنجليزية فقط، دون مسافات أو أرقام عربية.'));
        name.addEventListener('input',()=>name.setCustomValidity(''));
        form.addEventListener('submit',event=>{event.preventDefault();void sendForm(form,name);});
    } catch(error) {
        container.innerHTML=`<h2>النموذج غير متاح</h2><p>${escapeText(error instanceof Error?error.message:'تعذر فتح النموذج.')}</p>`;
    }
}
async function sendForm(form:HTMLFormElement,name:HTMLInputElement):Promise<void> {
    const normalizedName=name.value.normalize('NFC').trim().replace(/\s+/g,' ');
    const words=normalizedName.split(' ');
    const validName = normalizedName.length<=150 && words.length>=2 && words.every(w=>/[A-Za-z\p{Script=Arabic}]/u.test(w)) && /^[A-Za-z\p{Script=Arabic}\p{M} ]+$/u.test(normalizedName) && !/[\p{N}\p{P}\p{S}\p{Cf}]/u.test(normalizedName);
    if(!validName){name.setCustomValidity('اكتب الاسم من جزأين على الأقل بحروف عربية أو إنجليزية ومسافات فقط.');name.reportValidity();return;}
    const data=new FormData(form);const button=form.querySelector<HTMLButtonElement>('button')!;
    const errorBox=document.querySelector<HTMLElement>('#form-error')!;errorBox.textContent='';button.disabled=true;
    try {
        const choice:ChoicePayload={iqama:String(data.get('iqama')),name:normalizedName,workSystem:data.get('workSystem') as ChoicePayload['workSystem']};
        const receipt=await formRequest<ChoiceReceipt>(choice);
        container.innerHTML=`<div class="success-symbol">✓</div><h2 style="text-align:center">تم تسجيل اختيارك</h2><p>رقم المتابعة</p><div class="link-box">${escapeText(receipt.receipt)}</div>`;
    }catch(error){errorBox.textContent=error instanceof Error?error.message:'تعذر الإرسال.';button.disabled=false;}
}
void openForm();
