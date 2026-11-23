import { api, esc } from '../api';
import { getLanguage, pickLang, t, type Language } from '../i18n';
import { getSession, registerSensitiveSessionClear } from '../state';

export interface EvaluationLabel {
  id: string;
  labelAr: string;
  labelEn: string;
  labelUr: string;
}

export interface EvaluationStaffCard {
  id: string;
  name: string;
  cities: string[];
  departments: string[];
  roles: string[];
  apps: string[];
  active?: boolean;
  excluded?: boolean;
  pendingRoleApproval?: boolean;
}

export interface EvaluationSchema {
  schema: string;
  roundId: string;
  title: string;
  noticeAr?: string;
  noticeEn?: string;
  noticeUr?: string;
  cities: string[];
  apps: string[];
  staff: EvaluationStaffCard[];
  identityFields?: EvaluationLabel[];
  staffRatingMin?: number;
  staffRatingMax?: number;
  appRatingMin?: number;
  appRatingMax?: number;
  staffDimensions: EvaluationLabel[];
  staffReasons: EvaluationLabel[];
  appReasons: EvaluationLabel[];
  factors: EvaluationLabel[];
  factorMode?: string;
  editPolicyPending?: boolean;
  uploadsPolicyPending?: boolean;
  allowedActions: string[];
  attachmentLimits?: {
    imagesPerCard?: number;
    voicePerCard?: number;
    voiceSeconds?: number;
    replyBytes?: number;
  };
}

export interface StaffCardAnswerDraft {
  reason: string;
  ratings: Record<string, number>;
  notes: string;
  complaint: boolean;
  complaintDetails: string;
}

export interface AppCardAnswerDraft {
  reason: string;
  overall: number;
  factorRatings: Record<string, number>;
  notes: string;
}

export interface MobileEvaluationDraft {
  token: string;
  page: 1 | 2;
  city: string;
  nationality: string;
  phone: string;
  workApps: string[];
  deptFilter: string;
  appFilter: string;
  staffAnswers: Record<string, StaffCardAnswerDraft>;
  appAnswers: Record<string, AppCardAnswerDraft>;
  error: string;
  receipt: string;
}

const drafts = new Map<string, MobileEvaluationDraft>();
const schemas = new Map<string, EvaluationSchema>();

function draftKey(riderIqama: string, roundToken: string): string {
  const iqama = String(riderIqama || '').trim();
  if (!iqama) {
    throw new Error('evaluation draft requires authenticated rider iqama');
  }
  return `${iqama}::${roundToken}`;
}

const PLATFORM_LABELS: Record<string, Record<Language, string>> = {
  Jahez: { ar: 'جاهز', en: 'Jahez', ur: 'جاہز' },
  Keeta: { ar: 'كيتا', en: 'Keeta', ur: 'کیٹا' },
  HungerStation: { ar: 'هنقرستيشن', en: 'HungerStation', ur: 'ہنگر اسٹیشن' },
  Ninja: { ar: 'نينجا', en: 'Ninja', ur: 'ننجا' },
  Chefz: { ar: 'ذا شفز', en: 'The Chefz', ur: 'دی شیفز' },
  Amazon: { ar: 'أمازون', en: 'Amazon', ur: 'ایمیزون' },
};

export const COMMON_NATIONALITIES: Record<Language, string[]> = {
  ar: ['سعودي', 'مصري', 'يمني', 'سوداني', 'باكستاني', 'هندي', 'بنغلاديشي', 'سوري', 'أردني', 'فلبيني'],
  en: ['Saudi', 'Egyptian', 'Yemeni', 'Sudanese', 'Pakistani', 'Indian', 'Bangladeshi', 'Syrian', 'Jordanian', 'Filipino'],
  ur: ['سعودی', 'مصری', 'یمنی', 'سوڈانی', 'پاکستانی', 'بھارتی', 'بنگلہ دیشی', 'شامی', 'اردنی', 'فلپائنی'],
};

export function localizeApp(app: string, lang: Language = getLanguage()): string {
  return PLATFORM_LABELS[app]?.[lang] || app;
}

export function getOrInitDraft(
  token: string,
  schema: EvaluationSchema,
  defaults?: { city?: string; phone?: string; riderIqama?: string }
): MobileEvaluationDraft {
  const iqama = String(defaults?.riderIqama || getSession()?.iqama || '').trim();
  const key = draftKey(iqama, token);
  let d = drafts.get(key);
  if (!d) {
    const defaultCity =
      defaults?.city && schema.cities.includes(defaults.city)
        ? defaults.city
        : schema.cities.length === 1
        ? schema.cities[0]
        : '';
    d = {
      token,
      page: 1,
      city: defaultCity,
      nationality: '',
      phone: defaults?.phone || '',
      workApps: [],
      deptFilter: 'ALL',
      appFilter: 'ALL',
      staffAnswers: {},
      appAnswers: {},
      error: '',
      receipt: '',
    };
    drafts.set(key, d);
  }
  return d;
}

export function setEvaluationSchemaCache(token: string, schema: EvaluationSchema): void {
  schemas.set(token, schema);
}

export function getEvaluationSchemaCache(token: string): EvaluationSchema | undefined {
  return schemas.get(token);
}

export function clearEvaluationDrafts(): void {
  drafts.clear();
  schemas.clear();
}

registerSensitiveSessionClear(clearEvaluationDrafts);

export function getCityStaffCards(schema: EvaluationSchema, city: string): EvaluationStaffCard[] {
  if (!city) return [];
  return (schema.staff || []).filter((s) => !s.excluded && s.cities.includes(city));
}

export function isStaffCardComplete(
  schema: EvaluationSchema,
  ans: StaffCardAnswerDraft | undefined
): boolean {
  if (!ans) return false;
  if (ans.complaint && (ans.complaintDetails || '').trim().length < 5) return false;
  if (ans.reason !== '') {
    return schema.staffReasons.some((r) => r.id === ans.reason);
  }
  return schema.staffDimensions.every((dim) => {
    const v = Number(ans.ratings?.[dim.id]);
    return Number.isInteger(v) && v >= (schema.staffRatingMin ?? 1) && v <= (schema.staffRatingMax ?? 10);
  });
}

export function isAppCardComplete(
  schema: EvaluationSchema,
  ans: AppCardAnswerDraft | undefined
): boolean {
  if (!ans) return false;
  if (ans.reason !== '') {
    return schema.appReasons.some((r) => r.id === ans.reason);
  }
  const v = Number(ans.overall);
  return Number.isInteger(v) && v >= (schema.appRatingMin ?? 1) && v <= (schema.appRatingMax ?? 5);
}

export function buildEvaluationPayload(
  schema: EvaluationSchema,
  draft: MobileEvaluationDraft,
  identity?: { iqama?: string; name?: string }
): Record<string, unknown> {
  const cityStaff = getCityStaffCards(schema, draft.city);
  return {
    ...(identity?.iqama ? { iqama: identity.iqama } : {}),
    ...(identity?.name ? { name: identity.name } : {}),
    city: draft.city,
    nationality: draft.nationality.trim(),
    phone: draft.phone.trim(),
    workApps: draft.workApps,
    staffAnswers: cityStaff.map((s) => {
      const ans = draft.staffAnswers[s.id] || {
        reason: '',
        ratings: {},
        notes: '',
        complaint: false,
        complaintDetails: '',
      };
      const reason = ans.reason || '';
      return {
        staffId: s.id,
        reason,
        ...(reason === '' ? { ratings: ans.ratings } : {}),
        notes: (ans.notes || '').trim(),
        complaint: Boolean(ans.complaint),
        complaintDetails: ans.complaint ? (ans.complaintDetails || '').trim() : '',
        attachments: [],
      };
    }),
    appAnswers: (schema.apps || []).map((app) => {
      const ans = draft.appAnswers[app] || { reason: '', overall: 0, factorRatings: {}, notes: '' };
      const reason = ans.reason || '';
      return {
        app,
        reason,
        ...(reason === ''
          ? { overall: Number(ans.overall), factorRatings: ans.factorRatings || {} }
          : {}),
        notes: (ans.notes || '').trim(),
        attachments: [],
      };
    }),
  };
}

export function renderEvaluationSurveyHtml(
  schema: EvaluationSchema,
  draft: MobileEvaluationDraft
): string {
  const lang = getLanguage();
  const notice = pickLang(schema, 'notice', lang) || schema.noticeAr || '';
  const token = esc(draft.token);

  if (draft.receipt) {
    return `
      <div class="mob-card" data-eval-receipt="${esc(draft.receipt)}">
        <div class="notice good">
          <strong>✓ ${esc(t('evalSubmittedReceipt'))}</strong>
          <div class="number" style="margin-top:6px;word-break:break-all">${esc(draft.receipt)}</div>
        </div>
      </div>
    `;
  }

  const cityStaff = getCityStaffCards(schema, draft.city);
  const departments = [...new Set(cityStaff.flatMap((s) => s.departments || []))];
  const staffApps = [...new Set(cityStaff.flatMap((s) => s.apps || []))];

  const visibleStaff = cityStaff.filter(
    (s) =>
      (draft.deptFilter === 'ALL' || (s.departments || []).includes(draft.deptFilter)) &&
      (draft.appFilter === 'ALL' || (s.apps || []).includes(draft.appFilter))
  );

  const answeredStaffCount = cityStaff.filter((s) =>
    isStaffCardComplete(schema, draft.staffAnswers[s.id])
  ).length;
  const answeredAppCount = (schema.apps || []).filter((a) =>
    isAppCardComplete(schema, draft.appAnswers[a])
  ).length;

  const natOptions = COMMON_NATIONALITIES[lang] || COMMON_NATIONALITIES.ar;

  const page1Html = `
    <div data-eval-page="1">
      <div class="small muted" style="font-weight:700;margin-bottom:8px">${esc(t('evalPage1Title'))}</div>
      <div class="form-grid" style="margin-bottom:12px">
        <label>
          ${esc(t('fieldCity'))} *
          <select data-eval-city="${token}" required>
            <option value="">— ${esc(t('evalPickCityPrompt'))} —</option>
            ${(schema.cities || [])
              .map(
                (c) =>
                  `<option value="${esc(c)}" ${c === draft.city ? 'selected' : ''}>${esc(c)}</option>`
              )
              .join('')}
          </select>
        </label>
        <label>
          ${esc(t('evalNationalityLabel'))} *
          <input
            type="text"
            list="mob-eval-nat-${token}"
            data-eval-nationality="${token}"
            value="${esc(draft.nationality)}"
            placeholder="${esc(t('evalNationalityPlaceholder'))}"
            maxlength="100"
            required
          >
          <datalist id="mob-eval-nat-${token}">
            ${natOptions.map((n) => `<option value="${esc(n)}"></option>`).join('')}
          </datalist>
        </label>
        <label>
          ${esc(t('fieldPhone'))}
          <input
            type="tel"
            dir="ltr"
            inputmode="tel"
            data-eval-phone="${token}"
            value="${esc(draft.phone)}"
            placeholder="05XXXXXXXX"
            maxlength="16"
          >
        </label>
        <fieldset style="border:1px solid var(--line);border-radius:10px;padding:8px 10px;margin:0">
          <legend class="small" style="font-weight:700;padding:0 4px">${esc(t('evalWorkAppsLabel'))} *</legend>
          <div style="display:flex;flex-wrap:wrap;gap:8px">
            ${(schema.apps || [])
              .map(
                (app) => `
                  <label style="display:inline-flex;align-items:center;gap:4px;font-size:12px">
                    <input
                      type="checkbox"
                      data-eval-workapp="${token}"
                      value="${esc(app)}"
                      ${draft.workApps.includes(app) ? 'checked' : ''}
                    >
                    <span>${esc(localizeApp(app, lang))}</span>
                  </label>`
              )
              .join('')}
          </div>
        </fieldset>
      </div>

      ${
        !draft.city
          ? `<div class="notice warn small">${esc(t('evalPickCityPrompt'))}</div>`
          : `
            <div style="display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:6px;margin-bottom:8px">
              <span class="small" style="font-weight:700">${esc(t('evalCardsProgress'))}: <b class="number">${answeredStaffCount} / ${cityStaff.length}</b></span>
              ${
                departments.length > 1
                  ? `
                    <select data-eval-dept-filter="${token}" class="small" style="padding:4px 8px">
                      <option value="ALL">${esc(t('evalAllDepts'))}</option>
                      ${departments
                        .map(
                          (d) =>
                            `<option value="${esc(d)}" ${d === draft.deptFilter ? 'selected' : ''}>${esc(d)}</option>`
                        )
                        .join('')}
                    </select>`
                  : ''
              }
              ${
                staffApps.length > 1
                  ? `
                    <select data-eval-app-filter="${token}" class="small" style="padding:4px 8px">
                      <option value="ALL">${esc(t('evalAllApps'))}</option>
                      ${staffApps
                        .map(
                          (a) =>
                            `<option value="${esc(a)}" ${a === draft.appFilter ? 'selected' : ''}>${esc(localizeApp(a, lang))}</option>`
                        )
                        .join('')}
                    </select>`
                  : ''
              }
            </div>

            ${visibleStaff
              .map((s) => {
                const ans: StaffCardAnswerDraft = draft.staffAnswers[s.id] || {
                  reason: '',
                  ratings: {},
                  notes: '',
                  complaint: false,
                  complaintDetails: '',
                };
                const done = isStaffCardComplete(schema, ans);
                const roleDisplay = s.pendingRoleApproval
                  ? ['بانتظار موافقة غزال']
                  : s.roles || [];
                return `
                  <div class="mob-card" data-eval-staff-card="${esc(s.id)}" style="border:1px solid ${done ? 'var(--good, #2e7d32)' : 'var(--line)'};padding:12px;margin-bottom:10px">
                    <div style="display:flex;justify-content:space-between;align-items:flex-start;gap:6px;flex-wrap:wrap">
                      <div>
                        <strong style="font-size:14px">${esc(s.name)}</strong>
                        <div class="small muted" style="margin-top:2px">
                          ${(s.departments || []).map((d) => `<span class="tag">${esc(d)}</span>`).join(' ')}
                          ${(s.apps || []).map((a) => `<span class="tag">${esc(localizeApp(a, lang))}</span>`).join(' ')}
                        </div>
                        ${
                          roleDisplay.length
                            ? `<div class="small muted" style="margin-top:4px">${esc(t('evalRolesLabel'))}: ${esc(roleDisplay.join(' · '))}</div>`
                            : ''
                        }
                      </div>
                      <span class="tag ${done ? 'good' : 'warn'}">${esc(done ? t('evalAnsweredBadge') : t('evalPendingBadge'))}</span>
                    </div>

                    <div style="margin-top:8px">
                      <label class="small">
                        ${esc(t('evalSkipLabel'))}
                        <select data-eval-staff-reason="${token}" data-staff-id="${esc(s.id)}">
                          <option value="" ${ans.reason === '' ? 'selected' : ''}>★ ${esc(t('evalRateMode'))}</option>
                          ${schema.staffReasons
                            .map(
                              (r) =>
                                `<option value="${esc(r.id)}" ${ans.reason === r.id ? 'selected' : ''}>${esc(pickLang(r, 'label', lang))}</option>`
                            )
                            .join('')}
                        </select>
                      </label>
                    </div>

                    ${
                      ans.reason === ''
                        ? `
                          <div style="margin-top:8px;display:flex;flex-direction:column;gap:8px">
                            ${schema.staffDimensions
                              .map((dim) => {
                                const currentVal = ans.ratings?.[dim.id] || 0;
                                return `
                                  <div>
                                    <div class="small" style="display:flex;justify-content:space-between;margin-bottom:4px">
                                      <span>${esc(pickLang(dim, 'label', lang))}</span>
                                      <b class="number">${currentVal ? `${currentVal} / 10` : '—'}</b>
                                    </div>
                                    <div style="display:grid;grid-template-columns:repeat(5,minmax(0,1fr));gap:4px">
                                      ${Array.from({ length: 10 }, (_, idx) => idx + 1)
                                        .map(
                                          (n) => `
                                            <button
                                              type="button"
                                              class="small ${currentVal === n ? 'good' : 'secondary'}"
                                              data-eval-staff-rate="${token}"
                                              data-staff-id="${esc(s.id)}"
                                              data-dim-id="${esc(dim.id)}"
                                              data-score="${n}"
                                              aria-pressed="${currentVal === n}"
                                              style="padding:6px 2px;font-size:12px"
                                            >${n}</button>`
                                        )
                                        .join('')}
                                    </div>
                                  </div>`;
                              })
                              .join('')}
                          </div>`
                        : ''
                    }

                    <label class="small" style="display:block;margin-top:8px">
                      ${esc(t('evalNotesLabel'))}
                      <textarea
                        rows="2"
                        maxlength="2000"
                        data-eval-staff-notes="${token}"
                        data-staff-id="${esc(s.id)}"
                      >${esc(ans.notes)}</textarea>
                    </label>

                    <div style="margin-top:8px">
                      <button
                        type="button"
                        class="small ${ans.complaint ? 'warn' : 'secondary'}"
                        data-eval-staff-complaint-toggle="${token}"
                        data-staff-id="${esc(s.id)}"
                        aria-pressed="${ans.complaint}"
                      >
                        ${esc(ans.complaint ? t('evalComplaintActive') : t('evalComplaintBtn'))}
                      </button>
                      ${
                        ans.complaint
                          ? `
                            <label class="small" style="display:block;margin-top:6px">
                              ${esc(t('evalComplaintDetailsLabel'))} *
                              <textarea
                                rows="2"
                                minlength="5"
                                maxlength="2000"
                                data-eval-staff-complaint-details="${token}"
                                data-staff-id="${esc(s.id)}"
                                required
                              >${esc(ans.complaintDetails)}</textarea>
                            </label>`
                          : ''
                      }
                    </div>
                  </div>
                `;
              })
              .join('')}
          `
      }

      ${
        schema.uploadsPolicyPending
          ? `<div class="small muted" style="margin-top:6px">${esc(t('evalUploadsPending'))}</div>`
          : ''
      }
      ${draft.error ? `<div class="notice error small" style="margin-top:8px">${esc(draft.error)}</div>` : ''}

      <div style="margin-top:12px;display:flex;justify-content:flex-end">
        <button type="button" class="good" data-eval-next="${token}">
          ${esc(t('evalNextBtn'))}
        </button>
      </div>
    </div>
  `;

  const page2Html = `
    <div data-eval-page="2">
      <div style="display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:6px;margin-bottom:8px">
        <span class="small muted" style="font-weight:700">${esc(t('evalPage2Title'))}</span>
        <span class="small" style="font-weight:700">${esc(t('evalCardsProgress'))}: <b class="number">${answeredAppCount} / ${(schema.apps || []).length}</b></span>
      </div>

      ${(schema.apps || [])
        .map((app) => {
          const ans: AppCardAnswerDraft = draft.appAnswers[app] || {
            reason: '',
            overall: 0,
            factorRatings: {},
            notes: '',
          };
          const done = isAppCardComplete(schema, ans);
          return `
            <div class="mob-card" data-eval-app-card="${esc(app)}" style="border:1px solid ${done ? 'var(--good, #2e7d32)' : 'var(--line)'};padding:12px;margin-bottom:10px">
              <div style="display:flex;justify-content:space-between;align-items:center;gap:6px;flex-wrap:wrap">
                <strong style="font-size:14px">${esc(localizeApp(app, lang))}</strong>
                <span class="tag ${done ? 'good' : 'warn'}">${esc(done ? t('evalAnsweredBadge') : t('evalPendingBadge'))}</span>
              </div>

              <div style="margin-top:8px">
                <label class="small">
                  ${esc(t('evalSkipLabel'))}
                  <select data-eval-app-reason="${token}" data-app-id="${esc(app)}">
                    <option value="" ${ans.reason === '' ? 'selected' : ''}>★ ${esc(t('evalAppRateMode'))}</option>
                    ${schema.appReasons
                      .map(
                        (r) =>
                          `<option value="${esc(r.id)}" ${ans.reason === r.id ? 'selected' : ''}>${esc(pickLang(r, 'label', lang))}</option>`
                      )
                      .join('')}
                  </select>
                </label>
              </div>

              ${
                ans.reason === ''
                  ? `
                    <div style="margin-top:8px">
                      <div class="small" style="display:flex;justify-content:space-between;margin-bottom:4px">
                        <span>${esc(t('evalStarsLabel'))}</span>
                        <b class="number">${ans.overall ? `${ans.overall} / 5 ★` : '—'}</b>
                      </div>
                      <div style="display:grid;grid-template-columns:repeat(5,minmax(0,1fr));gap:4px">
                        ${[1, 2, 3, 4, 5]
                          .map(
                            (star) => `
                              <button
                                type="button"
                                class="small ${ans.overall === star ? 'good' : 'secondary'}"
                                data-eval-app-star="${token}"
                                data-app-id="${esc(app)}"
                                data-stars="${star}"
                                aria-pressed="${ans.overall === star}"
                                style="padding:6px 2px;font-size:12px"
                              >${star} ★</button>`
                          )
                          .join('')}
                      </div>

                      <div style="margin-top:10px">
                        <div class="small muted" style="font-weight:600;margin-bottom:4px">${esc(t('evalFactorsTitle'))}</div>
                        <div style="display:grid;gap:8px">
                          ${schema.factors
                            .map((f) => {
                              const stars = Number(ans.factorRatings?.[f.id] || 0);
                              return `
                                <div data-eval-app-factor-row="${token}" data-app-id="${esc(app)}" data-factor-id="${esc(f.id)}">
                                  <div class="small" style="display:flex;justify-content:space-between;margin-bottom:2px">
                                    <span>${esc(pickLang(f, 'label', lang))}</span>
                                    <b class="number">${stars ? `${stars} / 5 ★` : '—'}</b>
                                  </div>
                                  <div style="display:grid;grid-template-columns:repeat(5,minmax(0,1fr));gap:4px">
                                    ${[1, 2, 3, 4, 5]
                                      .map(
                                        (star) => `
                                          <button
                                            type="button"
                                            class="small ${stars === star ? 'good' : 'secondary'}"
                                            data-eval-app-factor-star="${token}"
                                            data-app-id="${esc(app)}"
                                            data-factor-id="${esc(f.id)}"
                                            data-stars="${star}"
                                            aria-pressed="${stars === star}"
                                            style="padding:4px 2px;font-size:11px"
                                          >${star} ★</button>`
                                      )
                                      .join('')}
                                  </div>
                                </div>`;
                            })
                            .join('')}
                        </div>
                      </div>
                    </div>`
                  : ''
              }

              <label class="small" style="display:block;margin-top:8px">
                ${esc(t('evalNotesLabel'))}
                <textarea
                  rows="2"
                  maxlength="2000"
                  data-eval-app-notes="${token}"
                  data-app-id="${esc(app)}"
                >${esc(ans.notes)}</textarea>
              </label>
            </div>
          `;
        })
        .join('')}

      ${draft.error ? `<div class="notice error small" style="margin-top:8px">${esc(draft.error)}</div>` : ''}

      <div style="margin-top:12px;display:flex;justify-content:space-between;gap:8px;flex-wrap:wrap">
        <button type="button" class="secondary" data-eval-prev="${token}">
          ${esc(t('evalPrevBtn'))}
        </button>
        <button type="button" class="good" data-eval-submit="${token}">
          ${esc(t('evalSubmitBtn'))}
        </button>
      </div>
    </div>
  `;

  return `
    <div class="evaluation-survey-box" data-eval-box="${token}">
      ${notice ? `<div class="notice good small" style="margin-bottom:10px">🛡️ ${esc(notice)}</div>` : ''}
      ${draft.page === 1 ? page1Html : page2Html}
    </div>
  `;
}

export function bindEvaluationSurveyEvents(
  root: HTMLElement,
  rerender: () => Promise<void> | void
): void {
  const ensureStaffAns = (draft: MobileEvaluationDraft, staffId: string): StaffCardAnswerDraft => {
    if (!draft.staffAnswers[staffId]) {
      draft.staffAnswers[staffId] = {
        reason: '',
        ratings: {},
        notes: '',
        complaint: false,
        complaintDetails: '',
      };
    }
    return draft.staffAnswers[staffId];
  };

  const ensureAppAns = (draft: MobileEvaluationDraft, appId: string): AppCardAnswerDraft => {
    if (!draft.appAnswers[appId]) {
      draft.appAnswers[appId] = {
        reason: '',
        overall: 0,
        factorRatings: {},
        notes: '',
      };
    }
    if (!draft.appAnswers[appId].factorRatings) draft.appAnswers[appId].factorRatings = {};
    return draft.appAnswers[appId];
  };

  root.querySelectorAll<HTMLSelectElement>('[data-eval-city]').forEach((sel) => {
    sel.onchange = () => {
      const token = sel.dataset.evalCity || '';
      const schema = schemas.get(token);
      if (!schema) return;
      const draft = getOrInitDraft(token, schema);
      draft.city = sel.value;
      draft.deptFilter = 'ALL';
      draft.appFilter = 'ALL';
      draft.error = '';
      rerender();
    };
  });

  root.querySelectorAll<HTMLInputElement>('[data-eval-nationality]').forEach((inp) => {
    inp.oninput = () => {
      const token = inp.dataset.evalNationality || '';
      const schema = schemas.get(token);
      if (!schema) return;
      const draft = getOrInitDraft(token, schema);
      draft.nationality = inp.value;
      draft.error = '';
    };
  });

  root.querySelectorAll<HTMLInputElement>('[data-eval-phone]').forEach((inp) => {
    inp.oninput = () => {
      const token = inp.dataset.evalPhone || '';
      const schema = schemas.get(token);
      if (!schema) return;
      const draft = getOrInitDraft(token, schema);
      draft.phone = inp.value;
    };
  });

  root.querySelectorAll<HTMLInputElement>('[data-eval-workapp]').forEach((chk) => {
    chk.onchange = () => {
      const token = chk.dataset.evalWorkapp || '';
      const schema = schemas.get(token);
      if (!schema) return;
      const draft = getOrInitDraft(token, schema);
      const val = chk.value;
      if (chk.checked) {
        if (!draft.workApps.includes(val)) draft.workApps.push(val);
      } else {
        draft.workApps = draft.workApps.filter((a) => a !== val);
      }
      draft.error = '';
    };
  });

  root.querySelectorAll<HTMLSelectElement>('[data-eval-dept-filter]').forEach((sel) => {
    sel.onchange = () => {
      const token = sel.dataset.evalDeptFilter || '';
      const schema = schemas.get(token);
      if (!schema) return;
      const draft = getOrInitDraft(token, schema);
      draft.deptFilter = sel.value;
      rerender();
    };
  });

  root.querySelectorAll<HTMLSelectElement>('[data-eval-app-filter]').forEach((sel) => {
    sel.onchange = () => {
      const token = sel.dataset.evalAppFilter || '';
      const schema = schemas.get(token);
      if (!schema) return;
      const draft = getOrInitDraft(token, schema);
      draft.appFilter = sel.value;
      rerender();
    };
  });

  root.querySelectorAll<HTMLSelectElement>('[data-eval-staff-reason]').forEach((sel) => {
    sel.onchange = () => {
      const token = sel.dataset.evalStaffReason || '';
      const staffId = sel.dataset.staffId || '';
      const schema = schemas.get(token);
      if (!schema || !staffId) return;
      const draft = getOrInitDraft(token, schema);
      const ans = ensureStaffAns(draft, staffId);
      ans.reason = sel.value;
      if (ans.reason !== '') ans.ratings = {};
      draft.error = '';
      rerender();
    };
  });

  root.querySelectorAll<HTMLButtonElement>('[data-eval-staff-rate]').forEach((btn) => {
    btn.onclick = () => {
      const token = btn.dataset.evalStaffRate || '';
      const staffId = btn.dataset.staffId || '';
      const dimId = btn.dataset.dimId || '';
      const score = Number(btn.dataset.score || 0);
      const schema = schemas.get(token);
      if (!schema || !staffId || !dimId || !score) return;
      const draft = getOrInitDraft(token, schema);
      const ans = ensureStaffAns(draft, staffId);
      ans.reason = '';
      ans.ratings[dimId] = score;
      draft.error = '';
      rerender();
    };
  });

  root.querySelectorAll<HTMLTextAreaElement>('[data-eval-staff-notes]').forEach((ta) => {
    ta.oninput = () => {
      const token = ta.dataset.evalStaffNotes || '';
      const staffId = ta.dataset.staffId || '';
      const schema = schemas.get(token);
      if (!schema || !staffId) return;
      const draft = getOrInitDraft(token, schema);
      ensureStaffAns(draft, staffId).notes = ta.value;
    };
  });

  root.querySelectorAll<HTMLButtonElement>('[data-eval-staff-complaint-toggle]').forEach((btn) => {
    btn.onclick = () => {
      const token = btn.dataset.evalStaffComplaintToggle || '';
      const staffId = btn.dataset.staffId || '';
      const schema = schemas.get(token);
      if (!schema || !staffId) return;
      const draft = getOrInitDraft(token, schema);
      const ans = ensureStaffAns(draft, staffId);
      ans.complaint = !ans.complaint;
      if (!ans.complaint) ans.complaintDetails = '';
      draft.error = '';
      rerender();
    };
  });

  root.querySelectorAll<HTMLTextAreaElement>('[data-eval-staff-complaint-details]').forEach((ta) => {
    ta.oninput = () => {
      const token = ta.dataset.evalStaffComplaintDetails || '';
      const staffId = ta.dataset.staffId || '';
      const schema = schemas.get(token);
      if (!schema || !staffId) return;
      const draft = getOrInitDraft(token, schema);
      ensureStaffAns(draft, staffId).complaintDetails = ta.value;
      draft.error = '';
    };
  });

  root.querySelectorAll<HTMLButtonElement>('[data-eval-next]').forEach((btn) => {
    btn.onclick = () => {
      const token = btn.dataset.evalNext || '';
      const schema = schemas.get(token);
      if (!schema) return;
      const draft = getOrInitDraft(token, schema);
      if (!draft.city || !draft.nationality.trim() || draft.workApps.length === 0) {
        draft.error = t('evalMissingIdentity');
        rerender();
        return;
      }
      const cityStaff = getCityStaffCards(schema, draft.city);
      const missingComplaint = cityStaff.some((s) => {
        const ans = draft.staffAnswers[s.id];
        return ans?.complaint && (ans.complaintDetails || '').trim().length < 5;
      });
      if (missingComplaint) {
        draft.error = t('evalMissingComplaint');
        draft.deptFilter = 'ALL';
        draft.appFilter = 'ALL';
        rerender();
        return;
      }
      const allComplete = cityStaff.every((s) =>
        isStaffCardComplete(schema, draft.staffAnswers[s.id])
      );
      if (!allComplete) {
        draft.error = t('evalUnansweredStaff');
        draft.deptFilter = 'ALL';
        draft.appFilter = 'ALL';
        rerender();
        return;
      }
      draft.error = '';
      draft.page = 2;
      rerender();
    };
  });

  root.querySelectorAll<HTMLButtonElement>('[data-eval-prev]').forEach((btn) => {
    btn.onclick = () => {
      const token = btn.dataset.evalPrev || '';
      const schema = schemas.get(token);
      if (!schema) return;
      const draft = getOrInitDraft(token, schema);
      draft.error = '';
      draft.page = 1;
      rerender();
    };
  });

  root.querySelectorAll<HTMLSelectElement>('[data-eval-app-reason]').forEach((sel) => {
    sel.onchange = () => {
      const token = sel.dataset.evalAppReason || '';
      const appId = sel.dataset.appId || '';
      const schema = schemas.get(token);
      if (!schema || !appId) return;
      const draft = getOrInitDraft(token, schema);
      const ans = ensureAppAns(draft, appId);
      ans.reason = sel.value;
      if (ans.reason !== '') {
        ans.overall = 0;
        ans.factorRatings = {};
      }
      draft.error = '';
      rerender();
    };
  });

  root.querySelectorAll<HTMLButtonElement>('[data-eval-app-star]').forEach((btn) => {
    btn.onclick = () => {
      const token = btn.dataset.evalAppStar || '';
      const appId = btn.dataset.appId || '';
      const stars = Number(btn.dataset.stars || 0);
      const schema = schemas.get(token);
      if (!schema || !appId || !stars) return;
      const draft = getOrInitDraft(token, schema);
      const ans = ensureAppAns(draft, appId);
      ans.reason = '';
      ans.overall = stars;
      draft.error = '';
      rerender();
    };
  });

  root.querySelectorAll<HTMLButtonElement>('[data-eval-app-factor-star]').forEach((btn) => {
    btn.onclick = () => {
      const token = btn.dataset.evalAppFactorStar || '';
      const appId = btn.dataset.appId || '';
      const factor = btn.dataset.factorId || '';
      const stars = Number(btn.dataset.stars || 0);
      const schema = schemas.get(token);
      if (!schema || !appId || !factor || !stars) return;
      const draft = getOrInitDraft(token, schema);
      const ans = ensureAppAns(draft, appId);
      ans.reason = '';
      if (ans.factorRatings[factor] === stars) delete ans.factorRatings[factor];
      else ans.factorRatings[factor] = stars;
      draft.error = '';
      rerender();
    };
  });

  root.querySelectorAll<HTMLTextAreaElement>('[data-eval-app-notes]').forEach((ta) => {
    ta.oninput = () => {
      const token = ta.dataset.evalAppNotes || '';
      const appId = ta.dataset.appId || '';
      const schema = schemas.get(token);
      if (!schema || !appId) return;
      const draft = getOrInitDraft(token, schema);
      ensureAppAns(draft, appId).notes = ta.value;
    };
  });

  root.querySelectorAll<HTMLButtonElement>('[data-eval-submit]').forEach((btn) => {
    btn.onclick = async () => {
      const token = btn.dataset.evalSubmit || '';
      const schema = schemas.get(token);
      if (!schema) return;
      const draft = getOrInitDraft(token, schema);
      const allAppsComplete = (schema.apps || []).every((a) =>
        isAppCardComplete(schema, draft.appAnswers[a])
      );
      if (!allAppsComplete) {
        draft.error = t('evalUnansweredApps');
        rerender();
        return;
      }
      btn.disabled = true;
      draft.error = '';
      try {
        const payload = buildEvaluationPayload(schema, draft);
        const res = await api<{ receipt: string }>(
          '/api/rider-mobile/evaluations/' + encodeURIComponent(token),
          payload
        );
        draft.receipt = res.receipt || 'OK';
        await rerender();
      } catch (ex: any) {
        btn.disabled = false;
        draft.error = ex?.message || 'تعذر إرسال التقييم';
        await rerender();
      }
    };
  });
}
