# SuperResScaler

Affiche n'importe quelle fenêtre (jeu fenêtré, jeu en « plein écran fenêtré », émulateur…)
étirée en plein écran via un shader, à la manière de [Magpie](https://github.com/Blinue/Magpie),
mais pensé pour les **modes super-résolution des écrans CRT** : 1280x240, 1920x240, 2560x240…

Sur un CRT en 1920x240, l'image physique est en 4:3 mais les pixels sont 6 fois plus hauts
que larges. Un jeu 320x240 (ou 640x480) affiché tel quel n'occupe qu'une bande étroite au
centre de l'écran. SuperResScaler capture la fenêtre et l'étire horizontalement pour remplir
tout l'écran : le jeu retrouve son ratio 4:3 sur le tube.

## Fonctionnement

```
fenêtre du jeu ──► Windows.Graphics.Capture ──► texture D3D11 ──► recadrage
   (reste dessous, garde le focus)                                    │
                                                                      ▼
écran 1920x240 ◄── swap chain flip-model ◄── pixel shader HLSL (Area, Nearest…)
                    fenêtre overlay plein écran, top-most, transparente aux clics
```

- **Capture** : `Windows.Graphics.Capture` (comme Magpie) — fonctionne même si la fenêtre est
  recouverte par l'overlay, sans la bordure jaune sous Windows 11.
- **Rendu** : Direct3D 11 (via [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows)),
  un triangle plein écran et un pixel shader compilé à l'exécution depuis `Shaders/*.hlsl`.
- **Affichage** : fenêtre popup *layered + transparent + noactivate* couvrant l'écran, swap chain
  flip-model avec objet d'attente de latence (1 image en file) et VSync. Le jeu garde le focus
  clavier/manette.

## Prérequis

- Windows 10 2004 ou plus récent (Windows 11 recommandé : pas de bordure jaune de capture).
- Une carte graphique Direct3D 11.
- Pour compiler : SDK .NET 10. Pour exécuter une build non autonome : runtime .NET 10 Desktop.

## Compilation

```bash
dotnet build SuperResScaler.sln -c Release
```

L'exécutable est dans `src/SuperResScaler/bin/Release/net10.0-windows10.0.22621.0/`.

Version autonome (un seul `.exe` + le dossier `Shaders`, rien à installer sur la borne) :

```bash
dotnet publish src/SuperResScaler -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## Utilisation

### Interface

1. Lancer `SuperResScaler.exe`.
2. Choisir la fenêtre du jeu dans la liste (ou utiliser **Fenêtre active dans 3 s** puis cliquer
   sur le jeu).
3. Régler le shader, le mode, le recadrage… Les changements sont enregistrés immédiatement et
   appliqués en direct pendant la mise à l'échelle.
4. **Mettre à l'échelle la fenêtre sélectionnée**. Arrêt avec le raccourci ou le bouton.

Réduire la fenêtre l'envoie dans la zone de notification.

### Raccourcis (modifiables dans `settings.json`)

| Raccourci | Action |
| --- | --- |
| `Alt+Shift+A` | démarrer la mise à l'échelle de la fenêtre active / l'arrêter |
| `Alt+Shift+S` | shader suivant (enregistré dans le profil) |
| `Alt+Shift+C` | oublier et refaire la détection des bandes noires |

### Profils par application

**Créer un profil pour cette application** associe les réglages courants à l'exécutable de la
fenêtre sélectionnée (par ex. `game.exe`). Le profil est ensuite utilisé automatiquement pour
cette application ; avec **Démarrer automatiquement**, la mise à l'échelle se lance dès que
l'application passe au premier plan (il suffit de laisser SuperResScaler tourner, par exemple
avec `--tray` au démarrage de Windows).

### Ligne de commande (front-end de borne : Attract-Mode, LaunchBox, RocketLauncher…)

Le mode ligne de commande attend la fenêtre, la met à l'échelle, et quitte quand elle se ferme :

```bash
SuperResScaler.exe --process game.exe --wait 60
SuperResScaler.exe --process Supermodel.exe --shader Nearest --autocrop
SuperResScaler.exe --title "Daytona" --crop 0,0,0,16 --delay 3
SuperResScaler.exe --process game.exe --mode fullscreen --aspect 4:3_letterbox
SuperResScaler.exe --process game.exe --aspect 4:3_crop --barrel --barrel-center 60
SuperResScaler.exe --process game.exe --resolution 1920x240@60
SuperResScaler.exe --process launcher.exe --persist
SuperResScaler.exe --foreground --delay 3
```

Exemple de lanceur `.bat` :

```bat
start "" "C:\Jeux\MonJeu\game.exe"
"C:\Outils\SuperResScaler\SuperResScaler.exe" --process game.exe --wait 60
```

`SuperResScaler.exe --help` affiche toutes les options, `--list-modes [n]` les modes
d'affichage disponibles (comme `SetResolution LIST`). Codes de sortie : `0` OK,
`2` fenêtre introuvable, `3` échec du démarrage.

> Ne lancez pas en même temps l'interface et le mode ligne de commande : les deux
> utiliseraient les mêmes raccourcis.

## Réglages

| Réglage | Rôle |
| --- | --- |
| **Shader** | filtre de mise à l'échelle (voir ci-dessous) |
| **Affichage** | *Plein écran étiré* ou *Plein écran au ratio (bandes noires)* — voir ci-dessous |
| **Ratio d'aspect** | `original`, `4:3`, `16:9`, `4:3_stretch`, `4:3_crop`… comme le linuxloader — voir ci-dessous |
| **Rognage** | formats `_crop` : position de la partie gardée, barillet (milieu non déformé, rayon) — voir ci-dessous |
| **Forme de l'écran** | forme physique de l'écran. `auto` : 4:3 pour un mode super-résolution (au plus 600 lignes et plus de 2 fois plus large que haut : 1280x240, 1920x240, 2560x240, 1920x480i…), pixels carrés sinon (écran LCD) |
| **Écran de sortie** | *Auto* = l'écran où se trouve la fenêtre du jeu |
| **Résolution** | mode d'affichage pendant la mise à l'échelle (ex. `1920x240@60`) — voir ci-dessous |
| **Zone client uniquement** | ignore la barre de titre et les bordures |
| **Recadrage manuel** | pixels à retirer de chaque côté (barres d'outils d'émulateur, bords parasites…) |
| **Recadrage automatique** | retire les bandes noires (jeu 4:3 centré dans une fenêtre plus large). La zone détectée ne fait que grandir pour éviter les sauts lors des scènes sombres |
| **Capturer le curseur** | inclut le pointeur de la souris dans l'image |
| **VSync** | synchro verticale (désactivée = tearing autorisé, latence minimale) |
| **Prévisualisation fenêtrée** | affiche le résultat dans une fenêtre (par ex. 1920x240) pour tester sur un écran de PC |

### Ratio d'aspect et bandes noires

Mêmes formats que `ASPECT_RATIO` / `DISPLAY_MODE` du linuxloader (`blitPresent`), en deux étapes :

1. **le cadre** (la forme du *Ratio d'aspect*, ou celle de la fenêtre pour `original`) remplit
   l'écran (*Plein écran étiré*, `FULLSCREEN_STRETCH`) ou garde sa forme sur l'écran physique
   avec des bandes noires (*Plein écran au ratio*, `FULLSCREEN`) ;
2. **l'image dans le cadre**, selon le format :

| Ratio d'aspect | Image dans le cadre |
| --- | --- |
| `original` | le cadre a la forme de la fenêtre (taille en pixels, pixels carrés) |
| `4:3`, `16:9`, `5:4`… (= `4:3_letterbox`) | entière, à sa propre forme, bandes noires là où les formes diffèrent (en parts du cadre, comme le linuxloader) |
| `4:3_stretch`, `16:9_stretch`… | remplit le cadre, déformée (ex. un jeu 256x224 affiché en 4:3) |
| `4:3_crop`, `16:9_crop`… | remplit le cadre à sa forme, ce qui dépasse est coupé (côtés si l'image est plus large, haut et bas sinon) |

Les noms du linuxloader (`4:3_LETTERBOX`, `16:9_STRETCH`, `4:3_CROP`…) sont acceptés tels quels,
et tout ratio fonctionne (`5:4_crop`, `8:7_stretch`…).

**Rognage** (formats `_crop`, `CROP_POSITION` / `CROP_BARREL` / `BARREL_CENTER` / `BARREL_RADIUS`) :

- **position** : -100 garde la gauche (ou le haut), 0 le milieu, 100 la droite (ou le bas) ;
- **barillet** : au lieu de couper, garde le milieu tel quel (même échelle et même partie de
  l'image que le rognage simple) et comprime les parties coupées sur les bords, comme une image
  enroulée sur un cylindre vu de face : toute l'image reste visible. *Milieu non déformé* = largeur
  du milieu en % du cadre (défaut 60, 100 = rognage simple) ; *rayon* = rayon du cylindre en % du
  cadre (défaut 50 : petit, la courbure se concentre au bord ; grand, compression presque
  uniforme). Rendu en bande de quadrilatères texturés (64 pas par bord), avec les formules de
  `barrelSetup` / `barrelSource` du linuxloader.

Contrairement au linuxloader, le calcul tient compte de la **forme physique de l'écran** : en
1920x240 sur un CRT 4:3, chaque pixel est 6 fois plus haut que large, sinon un jeu 4:3 « au
ratio » n'occuperait que 320 pixels de large au centre.

Exemples sur un CRT en 1920x240 (*Forme de l'écran* `auto`) :

| Fenêtre source | Affichage | Ratio d'aspect | Résultat |
| --- | --- | --- | --- |
| 640x480 | étiré ou au ratio | `original` ou `4:3` | plein écran (4:3) |
| 1280x720 | au ratio | `original` | 1920x180, bandes en haut et en bas |
| 1280x720 | étiré | `4:3` | 1920x180, bandes en haut et en bas |
| 1280x720 | étiré | `4:3_stretch` | plein écran, image 16:9 écrasée en 4:3 |
| 1280x720 | étiré | `4:3_crop` | plein écran, colonnes 160 à 1119 gardées |
| 1280x720 | étiré | `4:3_crop` + barillet | plein écran, toute l'image, bords comprimés |
| 256x224 | au ratio | `original` ou `4:3` | 2194x240 sur 2560x240, bandes sur les côtés (forme 8:7) |
| 256x224 | au ratio | `4:3_stretch` | plein écran (4:3) |
| 640x480 | au ratio | `16:9` | 1440x180 au centre, bandes tout autour |

### Changement de résolution

Comme `SetResolution.exe -w 1920 -h 240 -f 60`, le réglage **Résolution** (`--resolution`)
passe l'écran de sortie dans un mode d'affichage quand la mise à l'échelle démarre, puis
**remet la résolution d'origine du bureau** quand elle s'arrête : fermeture du jeu, raccourci,
bouton *Arrêter* ou sortie de SuperResScaler.

- Formats : `1920x240`, `1920x240@60`, `1280x480i` (entrelacé), `1280x480p@30`. Le mode choisi
  est le plus proche parmi ceux du pilote : même taille, progressif d'abord si rien n'est précisé,
  fréquence demandée à ±1 Hz (modes 59,94 Hz), sinon la fréquence actuelle, puis 60 Hz, 32 bits.
- La liste de l'interface et `--list-modes` montrent les modes du pilote (CRT Emudriver /
  Switchres doivent les avoir installés).
- Le changement est temporaire (`CDS_FULLSCREEN`, rien n'est écrit dans le registre). Si le
  retour au mode d'origine échoue, Windows reprend le mode enregistré dans le registre.
- Ignoré en *Prévisualisation fenêtrée*. Un changement pendant la mise à l'échelle s'applique au
  démarrage suivant.

Quand une fenêtre est sélectionnée, la ligne **Placement** de l'interface indique où l'image
sera affichée avec les réglages en cours.

Les réglages sont dans `settings.json` à côté de l'exécutable (ou dans
`%AppData%\SuperResScaler` si le dossier n'est pas accessible en écriture), avec le journal
`SuperResScaler.log`.

## Shaders

| Shader | Usage |
| --- | --- |
| **Area** (défaut) | moyenne exacte de la zone couverte : net en agrandissement, lignes moyennées en réduction (480 → 240) |
| **Nearest** | plus proche voisin, le plus net ; idéal en super-résolution pour une source 240 lignes |
| **LinearX_NearestY** | lissé horizontalement, lignes nettes : rendu « analogique » du CRT |
| **SharpBilinear** | pixels nets avec transitions douces aux ratios non entiers |
| **Bilinear** | lissage simple |
| **CatmullRom** | bicubique, pour la 3D / la vidéo |
| **Lanczos3** | le plus net des filtres lissés, pour la 3D |

Pour écrire son propre shader, voir [Shaders/README.md](src/SuperResScaler/Shaders/README.md).

## Tests

Tests unitaires (calcul du placement et des ratios) :

```bash
dotnet test SuperResScaler.sln
```

Auto-test de bout en bout :

```bash
SuperResScaler.exe --selftest C:\temp\srs-test
SuperResScaler.exe --selftest C:\temp\srs-test --selftest-overlay
```

Affiche une mire 320x240 encadrée de bandes noires, la capture, vérifie la détection des
bandes, rend chaque shader en 1920x240 et 1280x240, rend les formats d'aspect (bandes noires,
`_stretch`, `_crop`, barillet) en vérifiant des pixels précis, demande au pilote s'il accepte les
modes d'affichage sans les appliquer (`CDS_TEST`), lance une vraie session en prévisualisation
(et en plein écran avec `--selftest-overlay`, ~2 s) et vérifie les pixels affichés.
Résultats dans `selftest.txt` + images PNG.

## Limites

- Les jeux en **plein écran exclusif** ne peuvent pas être capturés : utiliser le mode fenêtré
  ou « plein écran fenêtré / borderless » du jeu.
- Une fenêtre **réduite** n'est pas capturée (l'overlay se masque jusqu'à sa restauration).
- Les **clics souris** traversent l'overlay et atteignent ce qui se trouve dessous aux mêmes
  coordonnées écran : ils ne sont pas remappés vers la position dans le jeu. Clavier et manettes
  ne sont pas concernés (le jeu garde le focus).
- La capture ajoute environ une image de latence (comme Magpie).
- Le contenu protégé (DRM) apparaît noir.

## Organisation du code

```
src/SuperResScaler/
  Program.cs                  point d'entrée (interface, ligne de commande, auto-test)
  App/ScalerController.cs     démarrage/arrêt, raccourcis, démarrage auto des profils
  App/HeadlessContext.cs      mode ligne de commande
  App/CommandLineOptions.cs   analyse des arguments
  App/SelfTest.cs             auto-test de bout en bout
  Engine/ScalingSession.cs    thread de rendu : capture -> shader -> présentation
  Engine/OverlayWindow.cs     fenêtre overlay (plein écran click-through ou prévisualisation)
  Engine/Geometry.cs          zone source, placement (formats du linuxloader, barillet)
  Native/DisplayModes.cs      modes d'affichage, changement et retour à la résolution d'origine
  Engine/BlackBarDetector.cs  détection des bandes noires
  Capture/WindowCapture.cs    Windows.Graphics.Capture
  Capture/WinRTInterop.cs     ponts WinRT <-> D3D11
  Rendering/Renderer.cs       device D3D11, compilation et exécution des shaders
  Rendering/SwapChainPresenter.cs  swap chain flip-model basse latence
  Config/                     options, profils, settings.json
  UI/MainForm.cs              interface WinForms
  Shaders/                    shaders HLSL (copiés à côté de l'exécutable)
tests/SuperResScaler.Tests/   tests unitaires xUnit (placement, ratios, barillet, modes d'affichage)
```
