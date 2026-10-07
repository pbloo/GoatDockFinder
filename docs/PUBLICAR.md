# Publicar o código e o site

Nada aqui foi feito por você ainda: o repositório local não tem remoto e o site não foi enviado a nenhum serviço.

## 1. Repositório no GitHub (na sua conta)

1. No GitHub, **New repository**: nome `GoatDockFinder`, sem README, sem .gitignore e sem licença (o projeto já tem os seus). Escolha público ou privado.
2. No terminal, na pasta do projeto:

```powershell
cd C:\Users\pablo\projetos\GoatDockFinder
git remote add origin https://github.com/SEU-USUARIO/GoatDockFinder.git
git push -u origin main
git push origin release/v1.0.0
```

3. Confira `git remote -v`. O primeiro push pede login no GitHub (o Git for Windows abre o navegador).
4. O workflow `.github/workflows/build-windows.yml` roda sozinho a cada push: compila, testa e gera o instalador como artefato (14 dias). Veja em **Actions**.

## 2. Release v1.0.0

Depois de testar o instalador e o Actions passar:

```powershell
git tag -a v1.0.0 -m "GoatDockFinder 1.0.0"
git push origin v1.0.0
```

No GitHub, **Releases → Draft a new release**, escolha a tag `v1.0.0`, anexe `release/GoatDockFinder-Setup.exe` (gerado por `build_release.ps1`) e cole no texto o hash SHA-256 de `docs/installer-validation.json`. Enquanto o instalador não for assinado, avise isso na descrição.

## 3. Site na Vercel

O site é estático e fica na pasta `vercel/` (HTML, CSS e JavaScript, sem build).

1. Abra `vercel/script.js` e preencha `REPO_URL` com o endereço do repositório (por exemplo `https://github.com/SEU-USUARIO/GoatDockFinder`). Sem isso, os links do GitHub e de download ficam escondidos e o site mostra "Download em breve". Faça commit e push.
2. Em [vercel.com](https://vercel.com), **Add New → Project** e importe o repositório.
3. Em **Root Directory**, escolha `vercel`. **Framework Preset**: Other. Deixe **Build Command** e **Output Directory** vazios. **Deploy**.
4. A Vercel publica em `https://<nome>.vercel.app` e republica a cada push em `main`. Domínio próprio: **Settings → Domains**.

Pela linha de comando (alternativa):

```powershell
npm i -g vercel
cd C:\Users\pablo\projetos\GoatDockFinder\vercel
vercel          # primeira vez: cria o projeto (prévia)
vercel --prod   # publica em produção
```

`vercel/vercel.json` já define cabeçalhos de segurança e cache. Não coloque segredos no site: tudo ali é público.
