# Codex instructions

Read [CLAUDE.md](CLAUDE.md) once for shared repository rules. Keep common guidance
there so Claude and Codex agree. Its file/process restrictions apply as
instructions; `.claude/settings.json` does not configure Codex permissions.

## Skills

OpenSpec uses the existing `openspec/` root. Claude's `/opsx:<action>` maps to:

| Action | Codex skill |
|---|---|
| propose | `$openspec-propose` |
| explore | `$openspec-explore` |
| apply | `$openspec-apply-change` |
| update | `$openspec-update-change` |
| sync | `$openspec-sync-specs` |
| archive | `$openspec-archive-change` |

Use the matching skill when invoked. `/log-progress` maps to `$log-progress`.
Use `$dotnet-expert` or `$db-architect` for relevant non-trivial work; they are
local guidance, not subagent configurations. Load only the relevant skill,
reuse context already read, and follow shared verification rules rather than
running duplicate builds. Keep generated OpenSpec skills managed by OpenSpec.
