# Como gerar o instalador assinado

O instalador gerado por `build_release.ps1` **não é assinado** por padrão, e o Windows (Controle de Aplicativo / Smart App Control, SmartScreen) pode bloqueá-lo ou avisar. Assinar exige um certificado que você ainda não tem; este guia mostra o caminho. Certificados autoassinados não resolvem a confiança pública.

## 1. Obter o certificado

Solicite um certificado **RSA de assinatura de código (Code Signing)** a uma certificadora confiável. Antes de contratar, confirme que ela aceita seu país e seu cadastro (pessoa física ou jurídica), que o certificado é compatível com Authenticode e Smart App Control e que dá para usar o SignTool no Windows. A certificadora valida sua identidade e explica como acessar a chave (token, serviço de assinatura ou provedor criptográfico).

Para projetos open source, veja as [opções oficiais de assinatura da Microsoft](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options), incluindo a SignPath Foundation (processo próprio, não integrado a este script). Não crie contas nem contrate serviços sem conferir condições e custos.

## 2. Preparar o computador

- .NET SDK 10 e o [Windows SDK](https://developer.microsoft.com/windows/downloads/windows-sdk/) com o SignTool.
- Certificado no repositório **Pessoal do usuário atual** (`Cert:\CurrentUser\My`), conforme o fornecedor.
- Token e PIN só no software do fornecedor. Nunca coloque senha, PFX ou chave privada no projeto, no repositório ou em conversas.

Liste só as informações públicas dos certificados:

```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Select-Object Subject, Thumbprint, NotAfter, HasPrivateKey
```

Copie o `Thumbprint` e confirme com a certificadora a URL de timestamp RFC3161. Descubra o SignTool x64:

```powershell
Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Filter signtool.exe -Recurse | Where-Object FullName -Match '\\x64\\' | Select-Object FullName
```

## 3. Gerar a versão assinada

```powershell
.\build_release.ps1 -Assinar `
    -CertificadoThumbprint 'THUMBPRINT_REAL_DE_40_CARACTERES' `
    -SignToolPath 'C:\Program Files (x86)\Windows Kits\10\bin\VERSAO_INSTALADA\x64\signtool.exe' `
    -TimestampUrl 'https://SERVIDOR_RFC3161_DA_CERTIFICADORA'
```

O script valida validade, finalidade de assinatura de código, RSA, chave privada e cadeia do certificado, assina GoatDock, GoatFinder e o instalador com SHA-256 (arquivo e timestamp) e verifica cada assinatura. `Directory.Build.targets` assina as DLLs próprias (`Goat*`) e as dependências (Ical.Net, NodaTime) antes de montar o arquivo único. Uma dependência nova sem assinatura interrompe o build e exige revisão.

## 4. Conferir

```powershell
Get-AuthenticodeSignature .\release\GoatDockFinder-Setup.exe | Select-Object Status, StatusMessage, SignerCertificate, TimeStamperCertificate
```

O esperado é `Valid`, com o fornecedor correto e timestamp. Teste instalar, atualizar e desinstalar no Windows 10 e 11 com a proteção ativa. Cada mudança nos binários exige novo build e nova assinatura.

Referências: [assinatura para Smart App Control](https://learn.microsoft.com/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control), [SignTool](https://learn.microsoft.com/windows/win32/seccrypto/signtool).
