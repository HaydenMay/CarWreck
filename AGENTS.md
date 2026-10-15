# CarWreck — Agent Instructions

Shared by Claude Code (via `CLAUDE.md`) and Codex/Sol. Project-specific workflows live in their own documents —
for new vehicles, follow `Docs/VehicleProduction/addacar-workflow.md` (the `addacar` skill). This file only holds
project-wide working rules.

## Scope Control and Usage Efficiency

These rules keep work focused and usage low. They do not reduce your autonomy: routine implementation decisions,
targeted bug fixes and required validation are already authorized within the assigned task.

1. **Respect the assigned scope.** Finish the explicitly requested milestone before any optional improvement.
   Finding a problem or a better design does not authorize expanding the assignment. Do not add features, redesign
   shared systems or do unrelated refactors on your own.
2. **Fix blockers, document the rest.** Investigate and fix anything that directly blocks the authorized task. For an
   unrelated or nonblocking issue: document it (severity and impact, plus a suggested fix) and continue the original
   task. Substantial shared-engine changes or architectural investigations need explicit user authorization first.
3. **Limit unsuccessful investigations.** After two substantially different failed approaches to the same problem,
   stop and report: what was tried, why it failed, what you learned and what should happen next. Do not keep trying
   slight variations without a clear justification.
4. **Test efficiently.** Use focused tests while implementing. Run full regression suites when the milestone requires
   them, when validating an approved shared-system change, or when asked; do not rerun a large suite after a minor
   change when a targeted test gives enough evidence. Long-running tests are fine when needed — do not poll them or
   issue extra commands while waiting. Never skip mandatory validation to save usage; report any required test you
   could not complete.
5. **Preserve progress.** Keep work organized and recoverable (a feature branch, small commits). Before a lengthy
   investigation, preserve a known-good state when practical. When stopping, give a concise handoff: completed work,
   remaining issues, test results, and relevant files or commits. Never merge into `main` without the user's
   authorization.
6. **Know when to stop.** Once the deliverable meets its acceptance criteria and required validation passes, finish.
   Do not keep optimizing, polishing, researching or expanding. Record further worthwhile improvements as suggestions
   for later.
7. **Don't over-ask.** Ask permission only for a substantial scope expansion, a meaningful architectural change, or an
   unusually expensive investigation outside the approved work. Otherwise proceed.
