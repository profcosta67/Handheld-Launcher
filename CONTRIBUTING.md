# Como contribuir

## Branches
- `master` é a versão estável. Só entra por Pull Request, com o build do CI verde (proteção ativa).
- Trabalho novo vai numa branch curta por tarefa ou versão: `feature/v0.16.4`, `fix/overlay-foco`, `chore/faxina`.
- Depois do merge, a branch é apagada.

## Commits
- Título curto, até 72 caracteres, no formato `tipo(área): o que mudou`.
  - Tipos: `feat` (recurso), `fix` (correção), `diag` (diagnóstico/log), `docs`, `ci`, `chore` (faxina), `refactor`, `test`.
  - Exemplo: `fix(overlay): corrige scroll do Quick Settings`.
- Explicações longas vão no corpo do commit ou nas notas da versão em `release-notes/`.
- Use sempre o mesmo nome e e-mail (o `.mailmap` junta as identidades antigas).

## Versão
- A versão fica só no arquivo `VERSION`. O projeto, o CI, o instalador e o User-Agent leem dele.
- Cada versão tem notas em `release-notes/<versão>.md` e uma linha no `CHANGELOG.md`.

## Beta e lançamento
1. Suba a branch: o CI compila e roda os testes.
2. Beta: Actions → **Release Beta** → Run workflow → escolha a branch. Sai como `vX.Y.Z-beta.N`, só pra quem ligou "Receber atualizações beta".
3. Teste no aparelho (Legion Go 2) antes de qualquer versão estável.
4. Estável: Pull Request pro `master`. O merge com o `VERSION` novo dispara o **Release** e cria a tag `vX.Y.Z`.

## Reportar problemas
Use os modelos de issue. No launcher, **Menu → Ajuda → Central de Ajuda → Reportar erro** gera um .zip com o log sem dados pessoais.
