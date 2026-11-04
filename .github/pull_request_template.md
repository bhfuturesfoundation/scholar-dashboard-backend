## Why

<!-- The problem, in a sentence or two. Link the issue or the matching web app PR. -->

## What changes

<!-- What a user or admin will notice, then the technical summary. -->

## Before merging

<!-- New or changed environment variables (set them on Railway first), migrations, merge order.
     Write "Nothing" if there is nothing. -->

## Testing

- [ ] `dotnet test Auth.Tests` passes
- [ ] Ran it locally end to end with the web app; scenarios below
- [ ] Migration (if any): applied, rolled back and re-applied on a local database with data
- [ ] New endpoints use `[Authorize(Roles = AppRoles.…)]`
- [ ] Docs updated (`docs/DEPLOYMENT.md`, `docs/SUPPORT_RUNBOOK.md`, web app help guide) or not needed

<!-- Scenarios you ran and what you saw. -->
