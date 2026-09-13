# `src/Ui/Diagnostics` — the user-docs factory

**The committed `docs/user` output is stale against the build.** A DocGen run with no source change
rewrites ~65 unrelated figures and pages (real drift, not id-churn and not the known nondeterministic
families), so `check-docs-current.sh` fails on a clean tree — revert them and commit only your own.
Detail, plus the two traps in adding a page to a NEW docs section, in the sibling `RESOLVED.md`.
