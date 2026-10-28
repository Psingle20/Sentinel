# Commit guidelines

Use Conventional Commits for every managed project. A commit header has this form:

```text
type(scope)!: concise imperative summary
```

- `scope` is optional. Use the owning app, service, package, or a stable area such as `docs`, `deploy`, or `deps`.
- `!` is optional and marks a breaking change.
- Keep the header at 100 characters or fewer, do not end it with a period, and describe one coherent change.
- Use the body to explain why the change was needed and any important tradeoffs. Wrap body lines near 72 characters when practical.
- Use footers for issue references, human co-authors, and `BREAKING CHANGE: ...` details.
- Do not add AI-agent attribution trailers. Commit messages must not contain trailers such as `Generated-by`, `Generated-with`, `Assisted-by`, or a `Co-authored-by`/`Signed-off-by` entry naming Codex, Claude, ChatGPT, Copilot, Gemini, Cursor, another AI agent, model, bot, or vendor.

## Types

- `feat`: new user-visible or system capability.
- `fix`: defect correction.
- `docs`: documentation-only change.
- `style`: formatting, whitespace, or other non-behavioral source changes. Do not use this merely because a change affects UI styling.
- `refactor`: internal restructuring without a feature or bug fix.
- `perf`: measurable performance improvement.
- `test`: test-only additions or corrections.
- `build`: build system, compiler, packaging, or dependency-build changes.
- `ci`: continuous-integration workflow or configuration changes.
- `chore`: repository maintenance that fits no more specific type, including routine dependency or tooling upkeep.
- `revert`: reversal of an earlier commit.

## Examples

```text
feat(ingestion): accept OpenTelemetry log batches
fix(runtime): prevent duplicate incident transitions
docs(architecture): explain executor trust boundary
style(api): apply formatter to endpoint mappings
refactor(agent-worker): isolate evidence ranking policy
perf(storage): batch observation writes
test(web): cover approval timeout state
build(deps): update dotnet sdk to 10.0.1
ci: add pull-request validation
chore(repo): refresh development tooling
feat(contracts)!: remove the legacy approval payload
```

For a breaking change, explain the impact and migration in the footer:

```text
feat(contracts)!: replace the approval response schema

BREAKING CHANGE: consumers must read decision.status instead of approved.
```

## Agent behavior

- Never create a commit unless the user asks for one.
- Never add an agent, AI-assistance, model, or tool attribution trailer. The commit must read as the user's project history, not as an agent transcript.
- Before committing, inspect the actual staged change and choose the most specific type and scope.
- Split unrelated work into separate commits instead of hiding it behind a broad `chore` commit.
- Run `.agents/validate-commit-message.ps1` against the proposed message before creating the commit.
- Put project-specific scope names or exceptions in `.agents/steering.md`; do not fork this shared type system.
