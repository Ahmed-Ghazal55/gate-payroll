import { api, esc, money } from '../api';

const STAGES: Record<string, string> = {
  under_review: 'تحت المراجعة',
  change_proposed: 'تحت المراجعة',
  approved: 'معتمد — في انتظار الصرف',
  paid: 'تم الصرف',
  rejected: 'مرفوض',
  cancelled: 'ملغي'
};

function getChip(a: any): [string, string] {
  if (a.status === 'open') return ['عند المشرف', 'warn'];
  if (a.status === 'declined') return ['مرفوض من المشرف', 'bad'];
  if (a.status === 'cancelled') return ['ملغي', ''];
  const label = STAGES[a.advanceStatus] || 'تحت المراجعة';
  const cls = a.advanceStatus === 'paid' ? 'good' :
              (a.advanceStatus === 'rejected' || a.advanceStatus === 'cancelled' ? 'bad' : 'warn');
  return [label, cls];
}

export async function renderRequestsHtml(): Promise<string> {
  const list: any[] = await api('/api/rider-mobile/requests');
  const busy = list.some(
    a => a.status === 'open' || (a.status === 'converted' && !['paid', 'rejected', 'cancelled'].includes(a.advanceStatus))
  );

  return `
    ${busy ? '<div class="mob-card small muted">لديك طلب قيد المتابعة؛ تقدر تطلب تاني بعد ما يخلص.</div>' : `
    <div class="mob-card">
      <h3 style="margin-top:0">طلب سلفة جديد</h3>
      <p class="small muted">طلبك بيروح لمشرفك، وهو اللي بيرفعه للحسابات.</p>
      <form id="mob-ask-form" class="form-grid">
        <label>
          النوع
          <select name="type">
            <option value="cash">نقدي</option>
            <option value="bank">تحويل بنكي</option>
          </select>
        </label>
        <label>
          المبلغ (ر.س)
          <input name="amount" type="number" min="1" max="100000" step="1" required inputmode="numeric">
        </label>
        <label>
          السبب
          <textarea name="reason" required minlength="3" maxlength="500" rows="2"></textarea>
        </label>
        <button class="good" style="margin-top:8px">إرسال الطلب للمشرف</button>
      </form>
    </div>`}
    <div class="mob-card" id="mob-asks">
      <h3 style="margin-top:0">طلباتي (${list.length})</h3>
      ${list.map(a => {
        const [label, cls] = getChip(a);
        return `
          <div style="border-bottom:1px solid var(--line);padding:10px 0" class="small" data-ask="${esc(a.id)}">
            <div style="display:flex;justify-content:space-between;align-items:center">
              <b class="number">${money(a.amount)} ر.س · ${a.type === 'bank' ? 'بنكي' : 'نقدي'}</b>
              <span class="tag ${cls}">${esc(label)}</span>
            </div>
            <div class="muted" style="margin-top:4px">
              ${esc(a.reason)} · <span class="number">${esc((a.createdAt || '').slice(0, 10))}</span>
            </div>
            ${a.status === 'declined' && a.note ? `<div class="notice error" style="margin-top:6px">${esc(a.note)}</div>` : ''}
            ${a.status === 'open' ? `<button class="small secondary" data-cancel-ask="${esc(a.id)}" style="margin-top:6px">إلغاء الطلب</button>` : ''}
          </div>`;
      }).join('') || '<p class="muted">لا توجد طلبات.</p>'}
    </div>
  `;
}
