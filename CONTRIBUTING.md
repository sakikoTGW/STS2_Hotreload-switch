# Contributing

1. Fork and clone the repo.
2. Set `STS2_PATH` or copy `local.props.example` → `local.props`.
3. Run `.\scripts\verify-build.ps1` before opening a PR.
4. Keep changes focused; avoid drive-letter paths in scripts (use `scripts/lib/Resolve-Sts2Path.ps1`).

CI builds **Core** and **StartupHook** without a game install. Full `ModHotReload` build is validated on a machine with STS2 installed.
