---
description: Execute multiple specs in dependency-aware waves (parallel within wave, sequential between waves)
allowed-tools: Read, Bash, Glob, Grep
argument-hint: (no arguments — wave plan is hardcoded for this project)
---

# Spec Wave Runner

## Wave Plan (project-specific dependency order)

| Wave | Mode | Specs |
|------|------|-------|
| 1 | sequential (single spec) | `core-ipc-foundation` |
| 2 | parallel | `ui-toolkit-shell`, `output-renderer-shell` |
| 3 | parallel | `camera-switcher-tab`, `character-selection-tab`, `stage-lighting-volume-tab` |

Wave 2 depends on Wave 1 completion. Wave 3 depends on Wave 2 completion.
Inside each wave, members run concurrently. Each member internally executes its `tasks.md` sequentially via `/kiro:spec-run`.

## Validate

For every spec listed in the Wave Plan:
- Verify `.kiro/specs/{feature}/` exists
- Verify `.kiro/specs/{feature}/tasks.md` exists
- Verify `.kiro/specs/{feature}/spec.json` shows `ready_for_implementation: true`

If any check fails, list all failing specs and abort with a clear message ("complete `/kiro:spec-tasks <feature>` first").

## Plan Preview

Read each `.kiro/specs/{feature}/tasks.md` and count unchecked leaf tasks (`- [ ] <id>` or `- [ ]* <id>`). Display:

```
Wave 1 (sequential): 1 spec
  - core-ipc-foundation         (N tasks)

Wave 2 (parallel):   2 specs
  - ui-toolkit-shell            (N tasks)
  - output-renderer-shell       (N tasks)

Wave 3 (parallel):   3 specs
  - camera-switcher-tab         (N tasks)
  - character-selection-tab     (N tasks)
  - stage-lighting-volume-tab   (N tasks)

Per-spec timeout: 4 hours (each spec runs its own /kiro:spec-run).
Per-task budget inside each spec: 30 minutes (inherited from /kiro:spec-run).
```

Ask the user to confirm before execution. If the user types `1`, `2`, or `3`, start from that wave (skip earlier waves). Default: start from Wave 1.

## Execute

For each wave in order:

1. **Announce wave start** — print wave number, mode, and member list.
2. **Launch members**:
   - Sequential mode: run one `claude -p` invocation, wait for completion.
   - Parallel mode: launch one `claude -p` invocation per member with `run_in_background: true`, then wait for **all** background tasks in this wave to finish before continuing.
3. **Per member, run** (using the Bash tool):

   ```bash
   unset CLAUDECODE && echo "" | claude -p "/kiro:spec-run <feature>" --max-turns 600 --enable-auto-mode --verbose
   ```

   - `unset CLAUDECODE` — required to allow nested `claude -p` (see `/kiro:spec-run` notes).
   - `--max-turns 600` — large budget because each `/kiro:spec-run` itself runs many tasks sequentially via further nested `claude -p` calls.
   - Set Bash `timeout` parameter to **14400000** ms (4 hours) per member.
   - Capture stdout/stderr and look for `OK` / `FAIL` per task in the trailing summary table.

4. **Wait for all wave members**, then collect per-spec status:
   - `ALL_OK` — every task in the spec returned OK.
   - `PARTIAL` — at least one task FAIL/TIMEOUT.
   - `ERROR` — the parent `claude -p` exited non-zero before producing a summary.

5. **Gate progression**:
   - If any member of the wave is not `ALL_OK`, **stop** and ask the user whether to:
     - (a) continue to the next wave anyway (downstream specs may break),
     - (b) re-run only failed members of this wave,
     - (c) abort.
   - On `ALL_OK` for the whole wave, proceed automatically to the next wave.

## Summary

After all waves finish (or the user aborts), display:

```
Wave 1: core-ipc-foundation        ALL_OK   (N/N tasks)
Wave 2: ui-toolkit-shell           ALL_OK   (N/N)
        output-renderer-shell      ALL_OK   (N/N)
Wave 3: camera-switcher-tab        ALL_OK   (N/N)
        character-selection-tab    PARTIAL  (N-1/N, failed: 5.3, 7.2)
        stage-lighting-volume-tab  ALL_OK   (N/N)
```

Then suggest next steps:
- All `ALL_OK`: run `/kiro:validate-impl <feature>` for each spec.
- Any failures: re-run the failed member with `/kiro:spec-run <feature>` after manual fix, or re-invoke `/kiro:spec-run-waves` and start from the failing wave.

## Notes

- The wave plan is intentionally hardcoded in this file. To rebalance waves (e.g., add a new spec, remove a finished one), edit the table above and the spec list in the Validate / Execute steps.
- Parallel members in the same wave are expected to be **independent in the file system and IPC topic space** — they may write to overlapping paths only when the design has explicitly partitioned ownership. If you suspect contention, downgrade that wave to sequential by moving members into separate single-spec waves.
- This command does not modify `tasks.md` itself; it only drives `/kiro:spec-run` per member, which performs the per-task git commits.
