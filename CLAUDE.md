@AGENTS.md

## Claude Code 固有

- コードやテストを追加・変更したら Slopwatch を実行する。判定基準とベースラインの扱いは `docs/coding-guidelines.md` の「静的解析（Slopwatch）」を正とする。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dotnet.ps1 slopwatch analyze -d . --fail-on warning
```
