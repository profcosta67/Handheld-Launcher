# Assinatura digital do instalador Windows

O workflow de release oferece assinatura Authenticode para os executáveis Windows antes de gerar os pacotes Velopack e para o instalador Setup.exe depois do empacotamento.

## Segredos necessários no GitHub

Em **Settings → Secrets and variables → Actions**, crie:

- `WINDOWS_SIGNING_CERTIFICATE_BASE64`: conteúdo Base64 do arquivo PFX de assinatura de código.
- `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`: senha do PFX.

O certificado precisa ser um certificado de **Code Signing** emitido por uma autoridade certificadora pública confiável, válido e com chave privada. Não use certificado autoassinado para distribuição pública.

Para converter o PFX em Base64 no PowerShell (na máquina segura onde o certificado está instalado):

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\caminho\certificado.pfx")) | Set-Clipboard
```

Cole o resultado no segredo `WINDOWS_SIGNING_CERTIFICATE_BASE64`. Nunca envie o PFX ou a senha para o repositório, issues, pull requests ou conversas.

## Verificação

Quando os dois segredos estiverem configurados, o workflow:

1. importa o PFX no runner temporário do GitHub Actions;
2. assina os executáveis e DLLs publicados com SHA-256 e carimbo de tempo;
3. gera os pacotes Velopack com os binários já assinados;
4. assina e verifica o `*Setup.exe` com SignTool.

Sem os segredos, a publicação continua sem assinatura e o workflow emite um aviso. Nesse caso, o Smart App Control ainda pode bloquear o instalador.

**Importante:** uma assinatura válida identifica o editor e protege a integridade do arquivo, mas não garante que o Smart App Control libere imediatamente o aplicativo. A decisão também depende da reputação do certificado/aplicativo e das políticas do Windows. Depois de configurar os segredos, publique uma nova versão e teste o arquivo baixado em uma instalação limpa do Windows antes de recomendar a instalação aos usuários.
