# Jira backlog

The backlog lives in Jira (project **SCRUM**, label `asset-dashboard`). The `Jira Key` column in
`jira-backlog.csv` maps each row to its ticket. Put the ticket key in commit messages (e.g.
`SCRUM-26: add Testcontainers integration tests`) so Jira links commits to issues.

The import instructions below are only needed to recreate the backlog in another Jira project.

`jira-backlog.csv` contains 6 epics and 42 tasks. Tasks already completed in this repository are
marked `Done`.

## Importing into Jira Cloud

1. Open **Jira → Filters/Issues → ⋯ → Import issues from CSV** (or **Settings → System → External
   system import → CSV**).
2. Select the target project.
3. Map the columns:
   - `Issue ID` → **Issue Id** (used only to link tasks to epics during import)
   - `Parent` → **Parent** (links each task to its epic)
   - `Issue Type`, `Summary`, `Description`, `Labels` → the fields with the same names
   - `Status` → **Status** (optional; map `To Do` / `Done` to your workflow's statuses, or leave
     it unmapped)
4. Run the import, then check that every task appears under its epic.
