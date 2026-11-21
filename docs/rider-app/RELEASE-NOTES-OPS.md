# Rider app — ops release notes

- Grant the three new mobile permissions (`mobile.complaints.confidential`, `riders.iban_data.approve_first`, `riders.iban_data.approve_second`) only to the intended reviewers; two different people must hold the first and second IBAN/data approval permissions (no blanket grant).
- Before go-live, check the admin riders screen for empty rider names and fix them through the approved path (mobile form submit rejects empty names with 409).
