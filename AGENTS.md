# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Project Overview

This is a **Spec-Driven Development (SDD) framework** called "Specify". It is not a traditional application — it is a meta-project template that provides Codex commands, templates, and PowerShell scripts for AI-assisted specification, planning, and implementation workflows.

There is no source code to build, no tests to run, and no package manager. The project consists entirely of Codex commands, markdown templates, and PowerShell automation scripts.

## Architecture

### Core Components

- **`.Codex/commands/`** — Nine speckit slash commands that drive the workflow
- **`.specify/templates/`** — Markdown templates for specs, plans, tasks, checklists, and constitutions
- **`.specify/scripts/powershell/`** — PowerShell automation (branch creation, prerequisite checks, plan setup, agent context updates)
- **`.specify/memory/constitution.md`** — Project governance template (non-negotiable principles)

### Workflow Sequence

The commands form a pipeline executed in order:

1. **`/speckit.specify`** — Create feature spec from natural language description. Creates a numbered branch (`NNN-short-name`), initializes `specs/<branch>/spec.md`, and generates a requirements checklist.
2. **`/speckit.clarify`** — Resolve ambiguities in the spec (max 5 targeted questions).
3. **`/speckit.plan`** — Generate technical plan (`plan.md`), research decisions (`research.md`), data model (`data-model.md`), and interface contracts (`contracts/`).
4. **`/speckit.tasks`** — Generate dependency-ordered `tasks.md` organized by user story phases.
5. **`/speckit.analyze`** — Read-only cross-artifact consistency analysis. Never modifies files.
6. **`/speckit.implement`** — Execute tasks phase-by-phase following TDD approach.

Supporting commands: `/speckit.constitution` (manage governance), `/speckit.checklist` (custom validation checklists), `/speckit.taskstoissues` (convert tasks to GitHub issues).

### Feature Directory Structure

Each feature lives in `specs/<NNN-short-name>/` and accumulates artifacts:

```
specs/001-feature-name/
├── spec.md              # Feature specification (what & why)
├── plan.md              # Technical implementation plan (how)
├── research.md          # Technical decisions and rationale
├── data-model.md        # Entity definitions and relationships
├── contracts/           # Interface contracts (APIs, CLIs, etc.)
├── tasks.md             # Dependency-ordered implementation tasks
├── quickstart.md        # Integration scenarios
└── checklists/          # Validation checklists
    └── requirements.md
```

### Key Scripts

All scripts are PowerShell (cross-platform via `pwsh`):

- **`create-new-feature.ps1`** — Creates numbered branch and feature directory. Auto-detects next number from branches and specs dirs. Supports `-Json`, `-ShortName`, `-Number` flags.
- **`check-prerequisites.ps1`** — Validates feature directory structure. Flags: `-Json`, `-RequireTasks`, `-IncludeTasks`, `-PathsOnly`.
- **`setup-plan.ps1`** — Initializes planning workflow, copies plan template.
- **`update-agent-context.ps1`** — Syncs agent-specific context files. Use `-AgentType Codex`.

### Conventions

- **Branch naming**: `NNN-short-name` (e.g., `001-user-auth`). Numbers are zero-padded to 3 digits.
- **Task IDs**: Sequential `T001`, `T002`, etc. Tasks use strict checklist format: `- [ ] [T001] [P?] [US?] Description with file path`
- **`[P]` marker**: Indicates parallelizable tasks (different files, no dependencies).
- **User story labels**: `[US1]`, `[US2]` etc. map to priority-ordered stories from spec.
- **Phase order**: Setup → Foundational → User Stories (P1, P2, P3...) → Polish
- **Specs are tech-agnostic**: `spec.md` describes WHAT and WHY, never HOW. No frameworks, languages, or APIs in specs.
- **Constitution is non-negotiable**: Violations are always CRITICAL severity in analysis. Changes require explicit constitution updates.
- **Max 3 `[NEEDS CLARIFICATION]` markers** per spec, prioritized by: scope > security > UX > technical details.
