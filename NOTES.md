# Suivi des demandes et du développement

Dépôt `superres_scaler` (https://github.com/njz3/superres_scaler.git), branche `main`, à jour sur `a2c6933`
(version 0.3.0, plus la version de test du 13/11). Identité git locale : `Benjamin <benjamin.maurin@gmail.com>`.

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
| 7 | 10/11 | Pull ; options de miroir X/Y de la sortie (projection) | ⚠️ fait : rendu vérifié hors écran (12 combinaisons), pas essayé sur un projecteur |
| 8 | 10/11 | Capturer un écran complet et l'afficher sur un autre, avec la numérotation de Windows | ⚠️ fait : capture, session et exclusion vérifiées par l'auto-test sur un seul écran ; pas essayé avec deux écrans ni sur une borne, numéro égal à celui de Windows non vérifiable automatiquement |
| 9 | 10/11 | Faire une release | ⚠️ 0.3.0 (`2230d17` + numéro de version), 145 tests OK ; auto-test 40/44 : les 4 contrôles qui lisent l'écran voient une image assombrie (×0,55), cause non identifiée, le même code passait 44/44 peu avant |
| 10 | 12/11 | Refaire l'archive 0.3.0 au format du 26/10 (exe dépendant de .NET + `Shaders/`, sans PDB) | ✅ `58b34cb`, 7,2 Mo, 145 tests, auto-test 44/44 ; l'archive autonome du 10/11 est laissée en place |
| 11 | 12/11 | Y a-t-il des choses à commiter localement ? | ✅ rien : arbre propre, `main` = `origin/main` |
| 12 | 13/11 | Écran virtuel dans SuperResScaler : créer, détruire, ajouter des modes, le capturer et l'afficher sur un autre écran avec shader, ratio… ; pilote embarqué dans l'exe | ⚠️ fait : 168 tests ; sur ce PC, créer/détruire (UAC), ajout/retrait de modes à chaud, capture suivant 640x480 → 1280x240 → 1024x384 ; pas essayé sur une borne |
| 13 | 13/11 | Vignette de l'écran virtuel en haut à droite, rafraîchie toutes les 0,5 s ; coût et retard de la capture ; un jeu TeknoParrot en 1920x1080 sur l'écran virtuel, affiché en 1280x240 sur le CRT ? | ⚠️ vignette faite et mesurée sur ce PC (177 tests), statistiques de capture dans le journal ; jeu TeknoParrot pas essayé |
| 14 | 13/11 | Capture par duplication de bureau pour Windows 10 1809 (écran virtuel des bornes), et choix de la méthode de capture sur un Windows plus récent | ⚠️ fait : 192 tests, auto-test complet ici et en mode 1809 simulé (`SUPERRESSCALER_NO_WGC=1`) ; pas essayé sur une vraie 1809 |
| 15 | 13/11 | Relancer l'écran virtuel arrêté (code 43) ; commit et push ; répondre à Image borne et lui demander d'essayer l'écran virtuel avec les jeux Raw Thrills | ⚠️ écran détruit puis recréé (UAC), actif ; demandes 12 à 14 commitées et poussées (`a2c6933`) ; version de test autonome ; message envoyé (réception non confirmée), essai à faire côté borne |
| 16 | 13/11 | Pas la peine d'inclure .NET 10 dans l'archive, il sera installé sur le PC | ✅ archive de test refaite au format du 26/10 (exe dépendant de .NET + `Shaders/`), 7,1 Mo, auto-test complet ; l'archive autonome est supprimée |
| 17 | 13/11 | Publier le correctif de l'auto-test pour la borne (aperçu trop large pour un écran de 640) | ⚠️ `26d22d7` poussé, archive `g26d22d7` sans .NET (7,1 Mo), auto-test complet ici ; message envoyé à Image borne, pas revérifié sur la borne |
| 18 | 13/11 | Passer en .NET 8 : les bornes l'ont déjà | ⚠️ fait : `net8.0-windows10.0.22621.0` et `RollForward` `Major` ; 195 tests, auto-test complet de l'exe (normal et 1809 simulé), interface rendue sous .NET 8.0.34 ; sortie console corrigée ; pas essayé sur une borne |
| 19 | 13/11 | Publier la version .NET 8 pour la borne | ⚠️ `a4f623b` poussé, archive `ga4f623b` (7,4 Mo), auto-test complet ici sous .NET 8 ; Image borne prévenue de ne pas installer l'exe `26d22d7` (.NET 10), réception non confirmée |

## Releases

Archives dans `E:\Arcade\emulateurs\dev\release`, nommées
`superres-scaler-v<version>-<AAAAMMJJ>-git-<commit>-win-x64.zip`.

| Date | Version | Commit | Type | Contenu | Taille | Vérification |
|:--|:--|:--|:--|:--|--:|:--|
| 23/10 | 0.1.0 | `d62ac1d` | autonome (runtime embarqué) | exe, `Shaders/`, `README.md`, PDB | 53 Mo | 78/78 tests, auto-test OK |
| 26/10 | 0.2.1 | `2f76561` | dépend du .NET 10 Desktop Runtime | exe, `Shaders/` | 7,6 Mo | 100/100 tests, auto-test 25/25 |
| 10/11 | 0.3.0 | `2230d17` | autonome (runtime embarqué), sur l'autre PC dans `emulateurs\releases` | exe, `Shaders/`, `settings.json`, `README.md`, `RELEASE.txt` | 50,7 Mo | 145/145 tests, auto-test 40/44 (demande 9) |
| 12/11 | 0.3.0 | `58b34cb` | dépend du .NET 10 Desktop Runtime | exe, `Shaders/` | 7,2 Mo | 145/145 tests, auto-test 44/44 |
| 13/11 | 0.3.0+a4f623b (test) | `a4f623b` | dépend du .NET 8 Desktop Runtime (ou plus récent), sur ce PC dans `emulateurs\releases` (`superres-scaler-0.3.0-20261113-ga4f623b-njz3-windows-x64.zip`) | exe, `Shaders/` | 7,4 Mo | 195/195 tests ; auto-test complet sur une copie sous .NET 8.0.34, en mode normal et en mode 1809 simulé |

Le 13/11, cette version de test a connu quatre archives :
- une archive autonome de 50,6 Mo (`a2c6933`), celle qu'Image borne a essayée sur Racecab1 ;
- une archive dépendante de .NET 10 (`be4d81b`), faite quand .NET 10 était prévu sur les bornes (demande 16) ;
- une archive dépendante de .NET 10 (`26d22d7`), avec la largeur d'aperçu de l'auto-test (demande 17) ;
- celle-ci, en .NET 8, que les bornes ont déjà (demandes 18 et 19).
Les trois premières sont supprimées. Un dossier extrait de l'archive `26d22d7` est resté dans `emulateurs\releases`,
créé par une autre session à 09:39 : son exe vise .NET 10 et ne démarre pas sur une borne qui n'a que .NET 8.

L'archive du 10/11 suit un autre nom (`superres-scaler-0.3.0-20261110-g2230d17-njz3-windows-x64.zip`, avec un
dossier déplié du même nom) et son `RELEASE.txt` annonce « rien à installer » : faux pour l'archive du 12/11.

## Points ouverts

- ⚠️ Nouvelle interface de la 0.2.0 (pages, profils, surveillance depuis la zone de notification) : pas essayée à
  la main, l'auto-test ne couvre pas ses pages.
- ⏳ Exe vraiment seul : intégrer les shaders dans l'exe (ressources). Pas demandé.
- ⏳ Exe plus petit : remplacer `Microsoft.Windows.SDK.NET.dll` par quelques interfaces COM écrites à la main dans
  `Capture/` (exe ~3 Mo au lieu de 28, archive ~1 Mo). Pas demandé.
- ⏳ Archive au format du 26/10 sans `settings.json` : au premier lancement l'exe en écrit un à côté de lui. Sur une
  borne sous UWF, l'exclure du filtre ou le fournir dans l'image (l'archive du 10/11 en livrait un par défaut).
- ⚠️ Assombrissement ×0,55 de l'auto-test du 10/11 (demande 9) : pas reproduit le 12/11, 44/44 sur ce PC avec le
  même code. Cause toujours inconnue.

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
- Depuis la demande 12, le pilote de l'écran virtuel est dans l'exe : pas de dossier à ajouter à l'archive.

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

### Écran virtuel (demande 12)
- Pilote Virtual Display Driver 25.7.23 (MIT, signé SignPath), embarqué tel quel en ressources ; originaux et
  SHA-256 dans `src/SuperResScaler/VirtualDisplayDriver/SOURCE.txt`. Son INF est en LF : `.gitattributes` empêche
  git de le convertir, sinon le catalogue signé ne correspond plus. Un test vérifie les SHA-256 des ressources.
- Créer / détruire : SuperResScaler se relance élevé (`--virtual-display-admin`), crée le périphérique
  `Root\MttVDD` comme devcon (SetupAPI) avec le pilote extrait dans un dossier temporaire, ou le supprime.
- Modes : `C:\VirtualDisplayDriver\vdd_settings.xml`, relu sans droits par la commande `RELOAD_DRIVER` sur le
  tube `\\.\pipe\MTTVirtualDisplayPipe`. Pas de `g_refresh_rate` : le pilote les ajoute à toutes les tailles.
  Windows arrondit 57,5 Hz en 58. Chaque relecture donne un nouveau `\\.\DISPLAYn` à l'écran virtuel.
- Constat : SetResolution 0.1.6 (celle des bornes) échoue sur un mode qui n'existe qu'à 57,5 Hz quand `-f`
  n'est pas donné, et rend quand même 0.

### Vignette et coût de la capture (demande 13)
- Vignette : copie GDI (`StretchBlt` en HALFTONE) de l'écran virtuel, réduite pendant la copie, hors du fil de
  l'interface, toutes les 0,5 s et seulement quand la fenêtre est visible. Placée dans la ligne du titre de
  chaque page (colonne de droite), qui ne prend de place que si l'écran virtuel existe. Super-résolutions en 4:3.
- Mesures sur ce PC (fenêtres de test à nous sur l'écran virtuel) : copie de la vignette 11 ms en 640x480,
  20 à 25 ms en 1280x240, 1920x240 et 1920x1080. Capture WGC de l'écran virtuel vers un aperçu 640x480 :
  image prise moins de 1 ms après sa composition en moyenne, 17 ms au plus ; 11 à 15 % d'un cœur.
- Le journal donne ces chiffres toutes les 10 s pendant une mise à l'échelle (`CaptureStatistics`).
- SuperResScaler ne capture pas sous Windows 10 1809 (bornes) : `CreateForWindow` / `CreateForMonitor`
  demandent la 1903. Il faudrait une capture par duplication de bureau (écran entier) pour 1809.

### Duplication de bureau (demande 14)
- `DuplicationCapture` (IDXGIOutput1::DuplicateOutput) : écran entier, Windows 8+, donc 1809. Choix dans le profil
  (Capture > Méthode de capture d'un écran) ou `--capture auto|wgc|duplication`. Auto = WGC dès 1903 (build 18362),
  sinon duplication. Une fenêtre reste capturée par WGC : sous 1809, choisir l'écran entier comme source.
- Le rendu se fait sur la carte de l'écran dupliqué (`Renderer(adapter)`). Sur ce PC, l'écran virtuel dépend de la
  NVIDIA, les deux écrans de l'Intel. `SupportedOSPlatformVersion` est passé à 10.0.17763.
- Constats :
  - la première image d'une duplication est noire : elle est ignorée, et un écran immobile est invité à se
    redessiner (`RedrawWindow`), l'image arrive en ~40 ms ;
  - un processus ne peut pas dupliquer deux fois le même écran à la fois (E_INVALIDARG) ;
  - débit brut 56 à 58 images/s en 640x480 et 1920x1080 ; en session vers un écran de l'Intel, 42 images/s en
    1920x1080 (copie entre cartes), âge moyen 8 à 15 ms contre moins de 1 ms avec WGC ;
  - un changement de mode perd la duplication (ACCESS_LOST), rouverte en ~250 ms ; une renumérotation aussi.
- **Écran virtuel : `RELOAD_DRIVER` fait planter le pilote** (événements 10111/10120 de
  DriverFrameworks-UserMode), Windows le relance : d'où le nouveau `\\.\DISPLAYn`. Après 5 relectures en ~2 min,
  Windows l'a arrêté (code 43). La page affiche maintenant cet état (CM_Get_DevNode_Status). Pendant une session
  vers l'autre carte, la NVIDIA est réinitialisée (DEVICE_REMOVED) : la session s'arrête avec un message.
- L'auto-test vérifie chaque méthode disponible ; en mode 1809 simulé, la mire est sans cadre (Windows 11 dessine
  sa bordure sur la zone client) et placée hors de l'ombre de la sortie.
- Racecab1, vraie 1809 (Windows 10 Entreprise LTSC 2019, build 17763.9487, PHL en 640x480), version autonome
  `a2c6933`, d'après Image borne : auto-test 41 PASS, 1 SKIP, 1 FAIL.
  - La duplication marche : image de l'écran entier, mire retrouvée, session par duplication.
  - L'échec venait du test : son aperçu de 1280x240 ne tient pas sur un écran de 640, et Windows le rétrécit.
  - La largeur de l'aperçu est maintenant `SelfTest.PreviewWidth`, 560 en 640x480 (`26d22d7`, pas encore revérifié sur la borne).

### Passage à .NET 8 (demande 18)
- Les bornes ont déjà le .NET 8 Desktop Runtime : cible `net8.0-windows10.0.22621.0` pour l'exe et les tests. Aucune
  ligne de code ne dépendait de .NET 9 ou 10, et Vortice 3.8.3 existe en .NET 8.
- .NET 8 n'a plus de mises à jour de sécurité depuis le 10/11/2026. `RollForward` = `Major` : sans .NET 8, l'exe
  démarre sur la version majeure suivante installée. Le journal note au démarrage la version de .NET et de Windows.
- Seule différence trouvée : sous .NET 8, `Console.OutputEncoding` lève une IOException quand la sortie d'un WinExe
  est redirigée (pas de console), ce qui faisait planter `--virtual-modes > fichier`. La sortie redirigée est
  maintenant écrite en UTF-8 par un `StreamWriter` sur la sortie standard.
- Vérifié sur ce PC : 195 tests sous .NET 8, auto-test complet de l'exe publié (normal et 1809 simulé),
  `--virtual-modes`, `--list-modes` et `--help` redirigés et dans un tube, interface rendue hors écran.
  Pas vérifiés : créer et détruire l'écran virtuel sous .NET 8 (UAC), et le démarrage sur .NET 10 sans .NET 8.
