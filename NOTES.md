# Suivi des demandes et du développement

Dépôt `superres_scaler` (https://github.com/njz3/superres_scaler.git), branche `main`, à jour sur `2f76561`
(version 0.2.1). Identité git locale : `Benjamin <benjamin.maurin@gmail.com>`.

Légende : ✅ fait et vérifié · 🔄 en cours · ⏳ à faire · ⚠️ fait, non vérifié

## Demandes

| # | Date | Demande | État |
|--:|:--|:--|:--|
| 1 | 23/10 | Pull, compiler une release, archive dans `dev/release` | ✅ 0.1.0 (`d62ac1d`), version autonome de 53 Mo |
| 2 | 26/10 | Pull, compiler une release, archive dans `dev/release` | ✅ 0.2.1 (`2f76561`) ; archive autonome remplacée ensuite (demandes 4 et 5) |
| 3 | 26/10 | Pourquoi l'exécutable fait presque 50 Mo ? | ✅ runtime .NET embarqué, voir journal |
| 4 | 26/10 | N'archiver que l'exécutable ; le PC devra avoir .NET 10 Desktop installé | ✅ exe dépendant de .NET + dossier `Shaders/` (indispensable), 7,6 Mo |
| 5 | 26/10 | Pas de PDB dans l'archive | ✅ |
| 6 | 26/10 | Mettre ces notes dans le fichier de suivi | ✅ ce fichier |

## Releases

Archives dans `E:\Arcade\emulateurs\dev\release`, nommées
`superres-scaler-v<version>-<AAAAMMJJ>-git-<commit>-win-x64.zip`.

| Date | Version | Commit | Type | Contenu | Taille | Vérification |
|:--|:--|:--|:--|:--|--:|:--|
| 23/10 | 0.1.0 | `d62ac1d` | autonome (runtime embarqué) | exe, `Shaders/`, `README.md`, PDB | 53 Mo | 78/78 tests, auto-test OK |
| 26/10 | 0.2.1 | `2f76561` | dépend du .NET 10 Desktop Runtime | exe, `Shaders/` | 7,6 Mo | 100/100 tests, auto-test 25/25 |

## Points ouverts

- ⚠️ Nouvelle interface de la 0.2.0 (pages, profils, surveillance depuis la zone de notification) : pas essayée à
  la main, l'auto-test ne couvre pas ses pages.
- ⏳ Exe vraiment seul : intégrer les shaders dans l'exe (ressources). Pas demandé.
- ⏳ Exe plus petit : remplacer `Microsoft.Windows.SDK.NET.dll` par quelques interfaces COM écrites à la main dans
  `Capture/` (exe ~3 Mo au lieu de 28, archive ~1 Mo). Pas demandé.

## Journal de développement

### Procédure de release (demandes 1, 2, 4 et 5)
- Compilation et tests :
  ```
  dotnet build SuperResScaler.sln -c Release
  dotnet test SuperResScaler.sln -c Release --no-build
  ```
- Publication en un seul exe dépendant de .NET (format retenu le 26/10) :
  ```
  dotnet publish src/SuperResScaler -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false -o publish
  ```
  `EnableCompressionInSingleFile=false` est obligatoire : le csproj l'active pour la version autonome et, sans
  runtime embarqué, le SDK refuse la compression (NETSDK1176).
- L'archive contient `SuperResScaler.exe` et `Shaders/`, sans PDB, README ni runtime. Le dossier `Shaders/` est
  indispensable : les `.hlsl` sont lus et compilés au lancement depuis le dossier de l'exe
  (`ShaderLibrary.Directory`) et l'exe n'en contient aucune copie ; sans eux, `Renderer` lève « Shader introuvable ».
- Vérification : `SuperResScaler.exe --selftest <dossier>` lancé depuis une copie exacte du contenu de l'archive.
  Pas `--selftest-overlay`, qui passe en plein écran.
- Piège : l'auto-test écrit `settings.json` et `SuperResScaler.log` à côté de l'exe. Refaire l'archive à partir de
  `publish/`, jamais du dossier qui a servi au test.
- Version autonome (demande 1 et première archive de la demande 2) : commande du README,
  `--self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`.

### Taille de l'exécutable (demande 3)
- Version autonome : 289 fichiers et 144 Mo une fois dépliée, compressés en un exe de 56 Mo :
  - runtime .NET et bibliothèques de base : ~95 Mo. Tout le framework est copié, même ce qui ne sert pas
    (`System.Private.Xml` 7,4 Mo, `System.Data.Common`…) ;
  - `Microsoft.Windows.SDK.NET.dll` : 25 Mo (6,4 Mo compressée). Projection WinRT tirée par la cible
    `net10.0-windows10.0.22621.0`, utilisée seulement dans `Capture/` pour `Windows.Graphics.Capture` ;
  - Windows Forms : 22 Mo ;
  - SuperResScaler et Vortice (Direct3D) : ~1,5 Mo, dont 0,2 Mo pour `SuperResScaler.dll`.
- Supprimer le code inutilisé à la publication (trimming) : le SDK le bloque pour Windows Forms (NETSDK1175).
  Non pris en charge par Microsoft, risque de casse à l'exécution : écarté.
- Version dépendante de .NET (retenue) : exe de 28 Mo, dont 25 Mo de `Microsoft.Windows.SDK.NET.dll` ;
  archive de 7,6 Mo.
