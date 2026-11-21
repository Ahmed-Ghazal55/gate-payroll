# Forms all-companies permission (post 0.29.1)

Unscoped staff (no company in financial scope) see form responses of every company **only** with `forms.responses.allCompanies`. Without it, lists and counts stay empty and out-of-scope reviews return 404.

## Ops note for Ghazal
- **Before deploy:** grant `forms.responses.allCompanies` to live unscoped reviewers who must keep seeing every company's form responses; otherwise they see empty lists after the release.
- Default grant in the catalog is SuperAdmin / Admin only. Scoped reviewers are unchanged.
