# Supervisors and platforms evaluation — spec (Ghazal, 2026-11-22)

Status: design agreed, NOT built. Branch `fix/cascading-column-filters` (already holds: cascading filters + per-form responses table, commits 6dfd25b, 09fe0f1, b5e0186).
Staff list RECEIVED 2026-11-22 (via planner). Kept outside the repo as seed data: shared notes folder `evaluation-staff-seed.json`. Names go into the DB via the admin screen, never into code files or commits.

## Why a special form type
The forms builder lacks: pages, 1–10/star ratings, image/voice upload, city-driven lists, per-item repeated questions, complaint flag. So: one new form type "تقييم المشرفين والمنصات", built once, reused per round.

## Rounds
- Manual only: management opens, shares, pauses, closes a round whenever it wants. No fixed schedule (keep the model open for a schedule later if cheap).
- One reply per rider per round. Editing allowed until the round closes, but limited (not every field) and rate-limited on the server so the server stays stable.

## Where it opens
- Rider app (signed-in rider, served as a server page like public forms; no APK update needed) AND a public link.
- Public link start: iqama + name + city + current work platform(s) + optional phone. Keep this as rider contact data (phone later used to invite riders to install the rider app; WhatsApp bulk invites if a suitable FREE way exists; phone verification only through a free service). Open item: research free OTP/WhatsApp options before building.

## Start of form
- Fixed notice: ratings and complaints are confidential, reviewed by management only; supervisors never see names or answers.
- Pick work city → filters supervisors. Platforms: ALL our platforms shown by default.

## Staff cards (update 2026-11-22)
- Cards cover staff, not only field supervisors: grouped by city → department (HR, finance & accounts, operations, platform follow-up teams per app, vehicles & movement & housing, Egypt office support/call centre).
- ONE card per person even when they hold several roles or teams; the card lists all roles; placed under each relevant city/department/app filter.
- Excluded (no cards): accounts manager, top management (2), and the finance reviewer with no rider contact. Keep an `excluded` flag in the staff list, not deletion.
- Ghazal's own card: role text to be derived from his role report (HTML in Downloads); do NOT show his reporting line in the UI.
- Before seeding, compare with Omar's ERP system users page (read only) for missing staff (people may be in the list but not in the system, not the reverse).
- Rider also records NATIONALITY (for matching staff to nationalities and for analysis/charts).

## Page 1 — supervisors (one card per supervisor of the chosen city, one by one, no "rate all")
- Every card MUST be answered: either rate, or pick a reason: «لم أتعامل معه / لا أعرفه / لم أعمل في منصته».
- Ratings 1–10: technical/professional level; treatment & manner (respect, way of talking); support given; response speed; solving problems / making work easier.
- Notes (text or voice note).
- «تقديم شكوى» button: marks complaint, required details (text or voice), attachments.
- «التالي» → page 2.

## Page 2 — platforms (one card per platform, one by one)
- Every card must be answered or marked «غير متاح في مكتب المدينة / لم أعمل عليها».
- Overall 1–5 stars, plus more reasons (derive from our performance/pay data): order volume; fair pay calculation; working hours count; shift times; flexibility; platform customer service; app problems (his view); is the work worth it; performance metrics we ask about.
- Every open text field also accepts a voice note.
- «إرسال».

## Uploads (files on server disk, DB keeps name+size only; Ghazal OK to use more disk space)
- Per card: max 2 images + 1 voice note. Images shrunk on phone (~300 KB). Voice ≤ 60 s (~1 MB). Whole reply ≤ 6 MB. Types: images + audio only.
- Open: retention period (proposed 12 months).

## Complaints
- A complaint also becomes a confidential complaint in the existing rider requests (RiderAsks complaint, targetKind supervisor): ONE place collects all complaints, followed up with status.
- Management sees who wrote each rating/complaint; supervisors see nothing.

## Access and output
- New permission "عرض التقييمات" (management only) + Excel export permission.
- Excel: every value in its own cell, no merged cells, all replies.
- Management screens (forms + responses area): settings (rounds, supervisor list, platforms), replies table, voice/image playback, ratings, charts and analysis per supervisor and per platform (averages, counts, complaints, trends). Define KPIs first using data-analysis + Figma design skills. Professional, complete, not complicated.

## Build order (proposal)
1. Data model: supervisor list (city, name, platforms, active), round, reply, card answers, attachments. 2. Admin: supervisor list + rounds. 3. Form page (2 pages) + uploads + limits + public link. 4. Complaints into rider requests. 5. Replies table + Excel. 6. Dashboard/KPIs (design first).

## Next session: start here
1. Read this file, then the shared notes folder `~/.config/Claude/side-session-notes/local_fcb3a12c-60c9-421d-8d73-b03138def3e5/` (`forms-and-filters-handover.md`, `evaluation-staff-seed.json`: 44 people, 4 excluded).
2. Read Ghazal's role report (HTML in `~/Downloads`, starts with «تقرير_المهام»; 813 KB, read in ranges/grep). Propose HIS card text (roles: Keeta team lead in both cities + Egypt team, from his career progression; never show his reporting line). Ask Ghazal to approve.
3. Compare the staff list with Omar's ERP system users (live, read only). Report anyone in the ERP users who has rider contact but is missing from the list. Ask Ghazal.
4. Then build in the order above (data model → admin → form → complaints → table/Excel → dashboard with KPIs designed first). Branch stays `fix/cascading-column-filters` unless the planner splits it.
