import { api, esc, money } from '../api';
import { t, pickLang, getLanguage, SALARY_BANKS } from '../i18n';

export type RequestTypeKey =
  | 'advance'
  | 'annual_leave'
  | 'iban_update'
  | 'data_update'
  | 'statement_request'
  | 'complaint'
  | 'appointment';

const REQUEST_TYPES: { key: RequestTypeKey; icon: string }[] = [
  { key: 'advance', icon: '💸' },
  { key: 'annual_leave', icon: '🌴' },
  { key: 'iban_update', icon: '🏦' },
  { key: 'data_update', icon: '🪪' },
  { key: 'statement_request', icon: '📄' },
  { key: 'complaint', icon: '🛡️' },
  { key: 'appointment', icon: '📅' }
];

export function getRequestChip(a: any): [string, string] {
  const reqType = a.requestType || 'advance';
  if (reqType === 'advance') {
    if (a.status === 'open') return [t('stageAtSupervisor'), 'warn'];
    if (a.status === 'declined') return [t('stageDeclinedSupervisor'), 'bad'];
    if (a.status === 'cancelled') return [t('stageCancelled'), ''];
    const adv = a.advanceStatus;
    if (adv === 'paid') return [t('stagePaid'), 'good'];
    if (adv === 'approved') return [t('stageApprovedAwaitPay'), 'warn'];
    if (adv === 'rejected') return [t('stageRejected'), 'bad'];
    if (adv === 'cancelled') return [t('stageCancelled'), 'bad'];
    return [t('stageUnderReview'), 'warn'];
  }

  switch (a.status) {
    case 'open':
      return [t('stageUnderReview'), 'warn'];
    case 'first_approved':
      return [t('stageFirstApproved'), 'warn'];
    case 'awaiting_rider':
      return [t('stageAwaitingRider'), 'warn'];
    case 'need_info':
      return [t('stageNeedInfo'), 'warn'];
    case 'approved':
      return [t('stageApproved'), 'good'];
    case 'rejected':
    case 'declined':
      return [t('stageRejected'), 'bad'];
    case 'cancelled':
      return [t('stageCancelled'), ''];
    default:
      return [t('stageUnderReview'), 'warn'];
  }
}

function renderServiceFormFields(selectedType: RequestTypeKey): string {
  const lang = getLanguage();
  const today = new Date().toISOString().slice(0, 10);
  const currentMonth = today.slice(0, 7);

  if (selectedType === 'annual_leave') {
    return `
      <input type="hidden" name="requestType" value="annual_leave">
      <label>
        ${esc(t('leaveTypeLabel'))}
        <select name="leaveType" required>
          <option value="annual">${esc(t('leaveAnnual'))}</option>
          <option value="emergency">${esc(t('leaveEmergency'))}</option>
          <option value="unpaid">${esc(t('leaveUnpaid'))}</option>
        </select>
      </label>
      <label>
        ${esc(t('startDateLabel'))}
        <input name="startDate" type="date" value="${esc(today)}" required>
      </label>
      <label>
        ${esc(t('endDateLabel'))}
        <input name="endDate" type="date" value="${esc(today)}" required>
      </label>
      <label>
        ${esc(t('destinationLabel'))}
        <select name="destination" required>
          <option value="inside_ksa">${esc(t('destInside'))}</option>
          <option value="outside_ksa">${esc(t('destOutside'))}</option>
        </select>
      </label>
      <label>
        ${esc(t('contactPhoneLabel'))}
        <input name="contactPhone" type="tel" dir="ltr" inputmode="tel" placeholder="05XXXXXXXX">
      </label>
      <label>
        ${esc(t('reasonLabel'))}
        <textarea name="reason" required minlength="2" maxlength="1000" rows="2"></textarea>
      </label>
    `;
  }

  if (selectedType === 'iban_update') {
    return `
      <input type="hidden" name="requestType" value="iban_update">
      <div class="notice warn">${esc(t('secondApproverNote'))}</div>
      <label>
        ${esc(t('ibanLabel'))}
        <input name="iban" id="mob-req-iban" required dir="ltr" placeholder="SA00 0000 0000 0000 0000 0000" autocomplete="off" spellcheck="false">
        <span class="small muted" id="mob-req-iban-hint">${esc(t('ibanHint'))}</span>
      </label>
      <label>
        ${esc(t('bankLabel'))}
        <select name="bankCode" id="mob-req-bank" required>
          ${SALARY_BANKS.map(
            b => `<option value="${esc(b.code)}">${esc(lang === 'en' ? b.en : b.ar)} — ${esc(b.swift)}</option>`
          ).join('')}
        </select>
      </label>
      <label>
        ${esc(t('reasonLabel'))}
        <textarea name="reason" maxlength="500" rows="2"></textarea>
      </label>
    `;
  }

  if (selectedType === 'data_update') {
    return `
      <input type="hidden" name="requestType" value="data_update">
      <div class="notice warn">${esc(t('secondApproverNote'))}</div>
      <label>
        ${esc(t('fieldToUpdateLabel'))}
        <select name="fieldToUpdate" required>
          <option value="phone">${esc(t('fieldPhone'))}</option>
          <option value="city">${esc(t('fieldCity'))}</option>
          <option value="housing">${esc(t('fieldHousing'))}</option>
          <option value="emergency_contact">${esc(t('fieldEmergency'))}</option>
        </select>
      </label>
      <label>
        ${esc(t('newValueLabel'))}
        <input name="newValue" required minlength="2" maxlength="300">
      </label>
      <label>
        ${esc(t('reasonLabel'))}
        <textarea name="reason" required minlength="2" maxlength="1000" rows="2"></textarea>
      </label>
    `;
  }

  if (selectedType === 'statement_request') {
    return `
      <input type="hidden" name="requestType" value="statement_request">
      <label>
        ${esc(t('statementScopeLabel'))}
        <select name="statementScope" required>
          <option value="monthly_payslip">${esc(t('scopeMonthly'))}</option>
          <option value="full_account">${esc(t('scopeFull'))}</option>
          <option value="debt_clearance">${esc(t('scopeDebt'))}</option>
        </select>
      </label>
      <label>
        ${esc(t('periodFromLabel'))}
        <input name="periodFrom" type="month" value="${esc(currentMonth)}" required>
      </label>
      <label>
        ${esc(t('periodToLabel'))}
        <input name="periodTo" type="month" value="${esc(currentMonth)}" required>
      </label>
      <label>
        ${esc(t('reasonLabel'))}
        <textarea name="reason" maxlength="500" rows="2"></textarea>
      </label>
    `;
  }

  if (selectedType === 'complaint') {
    return `
      <input type="hidden" name="requestType" value="complaint">
      <div class="notice good">${esc(t('confidentialNote'))}</div>
      <label>
        ${esc(t('complaintTargetLabel'))}
        <select name="targetKind" required>
          <option value="supervisor">${esc(t('targetSupervisor'))}</option>
          <option value="employee">${esc(t('targetEmployee'))}</option>
          <option value="operations">${esc(t('targetOperations'))}</option>
          <option value="finance">${esc(t('targetFinance'))}</option>
          <option value="other">${esc(t('targetOther'))}</option>
        </select>
      </label>
      <label>
        ${esc(t('subjectLabel'))}
        <input name="subject" required minlength="3" maxlength="200">
      </label>
      <label>
        ${esc(t('detailsLabel'))}
        <textarea name="details" required minlength="5" maxlength="2000" rows="3"></textarea>
      </label>
    `;
  }

  return `
    <input type="hidden" name="requestType" value="appointment">
    <label>
      ${esc(t('departmentLabel'))}
      <select name="department" required>
        <option value="hr">${esc(t('deptHr'))}</option>
        <option value="finance">${esc(t('deptFinance'))}</option>
        <option value="fleet">${esc(t('deptFleet'))}</option>
        <option value="operations">${esc(t('deptOperations'))}</option>
        <option value="management">${esc(t('deptManagement'))}</option>
      </select>
    </label>
    <label>
      ${esc(t('preferredDateLabel'))}
      <input name="preferredDate" type="date" value="${esc(today)}" required>
    </label>
    <label>
      ${esc(t('preferredSlotLabel'))}
      <select name="preferredSlot" required>
        <option value="morning">${esc(t('slotMorning'))}</option>
        <option value="afternoon">${esc(t('slotAfternoon'))}</option>
      </select>
    </label>
    <label>
      ${esc(t('reasonLabel'))}
      <textarea name="reason" required minlength="2" maxlength="1000" rows="2"></textarea>
    </label>
  `;
}

function renderActiveCampaignsCard(formsCatalog: any): string {
  const campaigns: any[] = formsCatalog?.activeCampaigns || [];
  if (!campaigns.length) return '';
  const riderPhone = String(formsCatalog?.rider?.phone || '');
  const riderCity = String(formsCatalog?.rider?.city || '');

  return `
    <div class="mob-card" id="mob-active-campaigns">
      <h3 style="margin-top:0">📝 ${esc(t('activeFormsTitle'))} (${campaigns.length})</h3>
      ${campaigns
        .map(c => {
          const title = pickLang(c, 'title') || c.title || '';
          const isIban = c.schema === 'iban-v1';
          const cities: any[] = Array.isArray(c.cities) ? c.cities : [];
          return `
            <div style="border-bottom:1px solid var(--line);padding:10px 0">
              <div style="display:flex;justify-content:space-between;align-items:center;gap:8px">
                <strong>${esc(title)}</strong>
                ${c.alreadySubmitted ? `<span class="tag good">✓ ${esc(c.submissionStatus || 'Submitted')}</span>` : ''}
              </div>
              <form class="form-grid" data-inapp-campaign="${esc(c.token)}" data-campaign-schema="${esc(c.schema)}" style="margin-top:8px">
                ${
                  isIban
                    ? `
                      <label>
                        ${esc(t('fieldPhone'))}
                        <input name="phone" type="tel" dir="ltr" inputmode="tel" value="${esc(riderPhone)}" required placeholder="05XXXXXXXX">
                      </label>
                      <label>
                        ${esc(t('fieldCity'))}
                        <select name="city" required>
                          <option value="">—</option>
                          ${cities
                            .map(city => {
                              const ar = String(city?.ar || '');
                              const en = String(city?.en || ar);
                              const label = getLanguage() === 'en' ? en : ar;
                              return `<option value="${esc(ar)}" ${ar === riderCity ? 'selected' : ''}>${esc(label)}</option>`;
                            })
                            .join('')}
                        </select>
                      </label>
                      <label>
                        ${esc(t('ibanLabel'))}
                        <input name="iban" required dir="ltr" placeholder="SA00 0000 0000 0000 0000 0000">
                      </label>
                      <label>
                        ${esc(t('bankLabel'))}
                        <select name="bankCode" required>
                          ${SALARY_BANKS.map(b => `<option value="${esc(b.code)}">${esc(b.ar)} — ${esc(b.swift)}</option>`).join('')}
                        </select>
                      </label>
                    `
                    : c.schema === 'custom-v1' && Array.isArray(c.fields)
                    ? c.fields
                        .filter((f: any) => f.type !== 'iqama')
                        .map(
                          (f: any) => `
                            <label>
                              ${esc(pickLang(f, 'label') || f.labelAr)}
                              <input name="${esc(f.id)}" ${f.required ? 'required' : ''}>
                            </label>`
                        )
                        .join('')
                    : `
                      <label>
                        <select name="workSystem" required>
                          <option value="tiers">نظام الشرائح (Tiers)</option>
                          <option value="commission">نظام العمولة (Commission)</option>
                        </select>
                      </label>
                    `
                }
                <button type="submit" class="small good">${esc(t('submitFormBtn'))}</button>
              </form>
            </div>
          `;
        })
        .join('')}
    </div>
  `;
}

export async function renderRequestsHtml(selectedType: RequestTypeKey = 'advance'): Promise<string> {
  const rawList: any = await api('/api/rider-mobile/requests');
  const list: any[] = Array.isArray(rawList) ? rawList : rawList?.items || [];

  let formsCatalog: any = null;
  try {
    formsCatalog = await api('/api/rider-mobile/forms');
  } catch {}

  const advanceBusy = list.some(
    a =>
      (a.requestType || 'advance') === 'advance' &&
      (a.status === 'open' || (a.status === 'converted' && !['paid', 'rejected', 'cancelled'].includes(a.advanceStatus)))
  );

  const typePills = `
    <div class="mob-card">
      <div class="small muted" style="margin-bottom:8px">${esc(t('requestsChooseType'))}</div>
      <div class="req-type-pills" role="tablist">
        ${REQUEST_TYPES.map(
          rt => `
            <button type="button" class="small ${selectedType === rt.key ? 'good' : 'secondary'}" data-req-type="${rt.key}" aria-selected="${selectedType === rt.key}">
              ${rt.icon} ${esc(t('req_' + rt.key))}
            </button>`
        ).join('')}
      </div>
    </div>
  `;

  const advanceCard =
    selectedType === 'advance'
      ? advanceBusy
        ? `<div class="mob-card small muted">${esc(t('advanceBusyNotice'))}</div>`
        : `
          <div class="mob-card">
            <h3 style="margin-top:0">💸 ${esc(t('advanceNewTitle'))}</h3>
            <p class="small muted">${esc(t('advanceNewHint'))}</p>
            <form id="mob-ask-form" class="form-grid">
              <label>
                ${esc(t('advanceTypeLabel'))}
                <select name="type">
                  <option value="cash">${esc(t('advanceCash'))}</option>
                  <option value="bank">${esc(t('advanceBank'))}</option>
                </select>
              </label>
              <label>
                ${esc(t('advanceAmountLabel'))}
                <input name="amount" type="number" min="1" max="100000" step="1" required inputmode="numeric">
              </label>
              <label>
                ${esc(t('reasonLabel'))}
                <textarea name="reason" required minlength="3" maxlength="500" rows="2"></textarea>
              </label>
              <button type="submit" class="good" style="margin-top:8px">${esc(t('advanceSubmit'))}</button>
            </form>
          </div>`
      : `
        <div class="mob-card">
          <h3 style="margin-top:0">${esc(t('req_' + selectedType))}</h3>
          <form id="mob-service-request-form" class="form-grid">
            ${renderServiceFormFields(selectedType)}
            <button type="submit" class="good" style="margin-top:8px">${esc(t('submitRequestBtn'))}</button>
          </form>
        </div>
      `;

  return `
    ${typePills}
    ${advanceCard}
    ${renderActiveCampaignsCard(formsCatalog)}
    <div class="mob-card" id="mob-asks">
      <h3 style="margin-top:0">🗂️ ${esc(t('myRequestsTitle'))} (${list.length})</h3>
      ${
        list
          .map(a => {
            const reqType = a.requestType || 'advance';
            const [label, cls] = getRequestChip(a);
            const isFromAdmin = a.direction === 'admin_to_rider';
            const title =
              reqType === 'advance'
                ? `${money(a.amount)} ${t('currencySar')} · ${a.type === 'bank' ? t('advanceBank') : t('advanceCash')}`
                : pickLang(a, 'title') || t('req_' + reqType);
            const timeline: any[] = Array.isArray(a.timeline) ? a.timeline : [];
            const replies: any[] = Array.isArray(a.replies) ? a.replies : [];
            const canCancel = a.status === 'open' || a.status === 'need_info' || a.status === 'first_approved';
            const canReply = !['cancelled', 'rejected', 'declined', 'paid'].includes(a.status);

            return `
              <div style="border-bottom:1px solid var(--line);padding:12px 0" class="small" data-ask="${esc(a.id)}" data-request-type="${esc(reqType)}">
                <div style="display:flex;justify-content:space-between;align-items:center;gap:6px;flex-wrap:wrap">
                  <div>
                    <b class="number" style="font-size:14px">${esc(title)}</b>
                    <span class="tag" style="margin-inline-start:4px">${esc(isFromAdmin ? t('dirFromAdmin') : t('dirFromRider'))}</span>
                    ${a.confidential ? `<span class="tag good" style="margin-inline-start:4px">🛡️</span>` : ''}
                    ${a.requiresSecondApprover ? `<span class="tag warn" style="margin-inline-start:4px">🔒 2x</span>` : ''}
                  </div>
                  <span class="tag ${cls}">${esc(label)}</span>
                </div>
                <div class="muted" style="margin-top:4px">
                  ${esc(a.reason)} · <span class="number">${esc((a.createdAt || '').slice(0, 10))}</span>
                </div>
                ${
                  (a.status === 'declined' || a.status === 'rejected' || a.status === 'need_info') && a.note
                    ? `<div class="notice error" style="margin-top:6px">${esc(a.note)}</div>`
                    : ''
                }
                ${
                  timeline.length
                    ? `
                      <div class="req-timeline" style="margin-top:8px;padding-inline-start:10px;border-inline-start:2px solid var(--line)">
                        <div class="muted" style="font-weight:600;margin-bottom:4px">${esc(t('timelineTitle'))}:</div>
                        ${timeline
                          .map(
                            step => `
                              <div style="margin-bottom:3px">
                                <span class="number muted">${esc(String(step.at || '').slice(0, 10))}</span> —
                                <span>${esc(pickLang(step, 'label') || step.labelAr || step.status)}</span>
                                ${step.note ? `<span class="muted"> (${esc(step.note)})</span>` : ''}
                              </div>`
                          )
                          .join('')}
                      </div>`
                    : ''
                }
                ${
                  replies.length
                    ? `
                      <div class="req-replies" style="margin-top:8px;background:var(--raised);padding:8px;border-radius:8px">
                        <div class="muted" style="font-weight:600;margin-bottom:4px">${esc(t('repliesTitle'))}:</div>
                        ${replies
                          .map(
                            rep => `
                              <div style="margin-bottom:4px">
                                <strong>${esc(rep.senderRole === 'rider' ? '👤' : '🏢')}:</strong>
                                <span>${esc(rep.message)}</span>
                              </div>`
                          )
                          .join('')}
                      </div>`
                    : ''
                }
                <div class="actions" style="flex-wrap:wrap;margin-top:8px">
                  ${
                    canCancel
                      ? `<button type="button" class="small secondary" data-cancel-ask="${esc(a.id)}">${esc(t('cancelRequestBtn'))}</button>`
                      : ''
                  }
                </div>
                ${
                  canReply
                    ? `
                      <form class="req-reply-form" data-reply-req="${esc(a.id)}" style="display:flex;gap:6px;margin-top:8px">
                        <input name="message" required minlength="2" maxlength="1000" placeholder="${esc(t('replyPlaceholder'))}" style="flex:1;padding:6px 10px;font-size:12px">
                        <button type="submit" class="small secondary">${esc(t('sendReplyBtn'))}</button>
                      </form>`
                    : ''
                }
              </div>`;
          })
          .join('') || `<p class="muted">${esc(t('requestsEmpty'))}</p>`
      }
    </div>
  `;
}
