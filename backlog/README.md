# Jira backlog

`jira-backlog.csv` contains 6 epics and 41 tasks. Tasks already completed in this repository are
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
