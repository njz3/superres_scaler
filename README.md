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
  recouverte par l'overlay, sans la bordure jaune sous Windows 11. Un écran entier peut aussi être
  capturé par la **duplication de bureau** DXGI, seule possible sous Windows 10 1809 (voir
  *Méthode de capture*).
- **Rendu** : Direct3D 11 (via [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows)),
  un triangle plein écran et un pixel shader compilé à l'exécution depuis `Shaders/*.hlsl`.
- **Affichage** : fenêtre popup *layered + transparent + noactivate* couvrant l'écran, swap chain
  flip-model avec objet d'attente de latence (1 image en file) et VSync. Le jeu garde le focus
  clavier/manette.

## Prérequis

- Windows 10 1809 ou plus récent (Windows 11 recommandé : pas de bordure jaune de capture).
  - **1809** (LTSC 2019, celle des bornes) : un écran entier seulement, par duplication de bureau ;
    pas de capture de fenêtre.
  - **1903** : capture d'une fenêtre (`Windows.Graphics.Capture`) ; le curseur est toujours capturé.
  - **2004** : choix du curseur, et la sortie posée sur l'écran capturé est retirée de la capture.
  - .NET 10 ne prend en charge Windows 10 1809 qu'en édition Entreprise ou LTSC.
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

Lancé sans paramètre, `SuperResScaler.exe` ouvre une fenêtre organisée comme celle de
[Magpie](https://github.com/Blinue/Magpie) : un menu à gauche, et des pages de réglages en cartes.

| Page | Contenu |
| --- | --- |
| **Accueil** | état de la mise à l'échelle (bouton *Arrêter*), surveillance des profils (interrupteur), raccourci, mise à l'échelle différée (*Démarrer*, puis cliquer sur le jeu), mise à l'échelle d'une fenêtre précise ou d'un écran complet, journal récent |
| **Profils → Par défaut** | réglages utilisés pour toute fenêtre sans profil |
| **Profils → *un jeu*** | les fenêtres auxquelles le profil s'applique, puis tous les réglages de rendu et de capture du jeu |
| **Profils → Nouveau profil** | choisir la fenêtre du jeu en cours (ou saisir son processus), un nom, et les réglages à copier |
| **Paramètres** | zone de notification, surveillance des profils, raccourcis (modifiables), numéros des écrans, dossiers |
| **À propos** | version, code source |

Les changements sont enregistrés immédiatement dans `settings.json` et appliqués en direct si le
profil modifié est celui de la mise à l'échelle en cours. La ligne **Placement** d'un profil
montre où l'image d'une fenêtre ouverte sera affichée avec ses réglages.

### Profils de jeux

Un profil stocke les réglages d'un jeu : shader, affichage, ratio d'aspect, rognage, résolution,
recadrage, etc. Il s'applique aux fenêtres choisies avec les mêmes filtres que BackForceFeeder :

- **Processus** : `game.exe` (le `.exe` est facultatif), jokers `*` et `?` (`game*.exe`),
  alternatives séparées par `|` (`game.exe|game_dx11.exe`) ; insensible à la casse ;
- **Titre de fenêtre** (facultatif) : un texte simple doit être contenu dans le titre ; avec des
  jokers, le motif porte sur tout le titre ; alternatives `|`. Les titres des fenêtres enfants
  comptent aussi (certains émulateurs titrent une fenêtre enfant).

Si plusieurs profils correspondent, celui qui a un filtre de titre l'emporte, puis celui dont le
processus est écrit sans joker.

### Zone de notification et surveillance des profils

Réduire la fenêtre, ou la fermer (réglage *Le bouton Fermer réduit dans la zone de notification*,
activé par défaut), l'envoie dans la zone de notification. SuperResScaler y **surveille les
processus et les titres de fenêtres**, comme BackForceFeeder : dès qu'une fenêtre correspond à un
profil dont la *mise à l'échelle automatique* est activée et qu'elle existe depuis le délai du
profil (1 s par défaut, le temps que le jeu prenne sa taille finale), la mise à l'échelle démarre
avec ce profil. Quand le jeu se ferme, elle s'arrête (et la résolution d'origine revient), puis la
surveillance reprend.

- La recherche a lieu toutes les secondes (*Intervalle entre deux recherches*) ; les petites
  fenêtres (moins de 160x120) et les fenêtres réduites sont ignorées ; si plusieurs fenêtres
  correspondent, la plus grande est choisie.
- Une mise à l'échelle arrêtée à la main (raccourci, bouton) n'est pas relancée tant que la
  fenêtre du jeu reste ouverte.
- Par défaut la surveillance ne tourne que lorsque la fenêtre est réduite ou dans la zone de
  notification (pour pouvoir régler un profil sans déclencher la mise à l'échelle) ; le réglage
  *Surveiller aussi quand cette fenêtre est affichée* la laisse toujours active.
- Clic sur l'icône : ouvrir ; clic droit : surveiller les profils oui/non, arrêter la mise à
  l'échelle, quitter.
- Pour une borne : `SuperResScaler.exe --tray` (ou le réglage *Démarrer dans la zone de
  notification*) au démarrage de Windows, avec un profil par jeu.

### Capturer un écran complet

Au lieu d'une fenêtre, SuperResScaler peut capturer tout un écran (par exemple un jeu qui s'affiche
sur un second écran) et le montrer sur un autre, avec le même ratio d'aspect, les mêmes shaders, le
miroir, le recadrage et le changement de résolution.

- **Une fois** : page *Accueil*, carte *Écran complet*. Les réglages par défaut s'appliquent, et la
  capture dure jusqu'à l'arrêt (bouton ou raccourci).
- **Pour un jeu** : dans son profil, section *Capture*, choisir l'écran dans **Source**. La fenêtre
  du profil, si elle existe, décide alors seulement de la fin : quand le jeu se ferme, la capture
  s'arrête. Le profil garde l'identifiant du moniteur, pas son numéro : il reste valable si Windows
  renumérote les écrans.
- **En ligne de commande** : `--display <écran>`. Seul, il démarre aussitôt ; avec `--process`, il
  attend le jeu et s'arrête avec lui.

Si **Écran de sortie** reste sur *Auto*, l'image part sur un autre écran que celui capturé (le
principal d'abord). S'il n'y a qu'un écran, ou si la sortie est le même écran, la sortie est retirée
de la capture pour ne pas se filmer elle-même.

**Numéros.** Windows ne donne pas accès aux numéros de *Paramètres > Affichage*. Le numéro proposé
est celui du pilote (le `n` de `\\.\DISPLAYn`, comme Hikaruloader), qui peut être différent. Le
bouton **Identifier** affiche le numéro sur chaque écran : s'il ne correspond pas à celui de Windows,
corrigez-le dans *Paramètres > Écrans*. `--display` et `--monitor` acceptent ce numéro ou une partie
du nom du moniteur (`--display DELL`).

### Méthode de capture

Un écran entier se capture de deux façons, au choix dans le profil (*Capture > Méthode de capture
d'un écran*) ou avec `--capture auto|wgc|duplication` :

| | Windows.Graphics.Capture | Duplication de bureau |
| --- | --- | --- |
| Windows | 10 1903 ou plus récent | 8 ou plus récent, donc 10 1809 |
| Fenêtre seule | oui | non : un écran entier |
| Curseur | au choix (2004 et plus) | jamais |
| Changement de mode de l'écran capturé | suivi | suivi (la duplication est rouverte) |
| Écran renuméroté (relecture des modes de l'écran virtuel) | la capture s'arrête | rouverte sur le nouveau `\\.\DISPLAYn` si le pilote graphique tient |
| Âge d'une image prise par la boucle de rendu (mesuré ici, écran virtuel) | < 1 ms en moyenne | 8 à 15 ms en moyenne |

**Auto** (par défaut) prend `Windows.Graphics.Capture` dès Windows 10 1903, la duplication avant.
Si `Windows.Graphics.Capture` est demandé sur une 1809, la duplication est utilisée, avec un
message. Une fenêtre est toujours capturée par `Windows.Graphics.Capture` : sous 1809, choisir comme
source l'écran entier où est le jeu.

La duplication capture ce que l'écran montre vraiment : une fenêtre posée sur le jeu est capturée
avec lui. Son rendu se fait sur la carte graphique de l'écran capturé. Sur le PC de développement
(Intel + NVIDIA), l'écran virtuel dépend de la carte NVIDIA et la sortie de l'Intel : Windows copie
chaque image de l'une à l'autre, et la capture d'un écran en 1920x1080 tombe à 42 images/s, contre
53 avec `Windows.Graphics.Capture`. Une borne à une seule carte n'a pas cette copie.

Pour essayer sur un Windows récent le comportement de la 1809, lancer SuperResScaler avec la
variable d'environnement `SUPERRESSCALER_NO_WGC=1` : il fait comme si `Windows.Graphics.Capture`
n'existait pas (auto-test compris).

### Écran virtuel de test

Pour essayer les super-résolutions sans borne ni émulation d'EDID, SuperResScaler peut créer un
**écran virtuel** : un moniteur sans matériel, avec les modes de votre choix, y compris ceux que la
carte graphique refuse (1280x240, 1024x384 à 57,5 Hz…). Un jeu, SetResolution ou SuperResScaler y
basculent comme sur un vrai écran, et la capture suit les changements de mode. L'écran est
invisible : on le voit en le mettant à l'échelle sur un vrai écran, avec shader, ratio d'aspect,
miroir, etc.

- **Page *Écran virtuel*** : *Créer* / *Détruire* (confirmation UAC : il faut les droits
  d'administrateur), *Mettre à l'échelle*, choix de sa résolution, liste de ses modes (ajouter,
  retirer, « modes des bornes »). Modifier les modes ne demande pas de droits : le pilote les relit
  aussitôt.
- **Vignette** : tant que l'écran virtuel existe, une petite image de son contenu s'affiche en haut
  à droite de chaque page, rafraîchie toutes les 0,5 s ; un clic ouvre sa page. Les super-résolutions
  (plus de 2,5 fois plus larges que hautes, comme 1280x240 ou 1024x384) y sont montrées en 4:3, comme
  sur le tube. Elle est copiée par GDI, hors du fil de l'interface : 11 ms pour un écran en
  640x480, 25 ms en 1920x1080 sur le PC de développement, soit 2 à 5 % d'un cœur. Rien n'est
  capturé quand la fenêtre est réduite ou dans la zone de notification.
- **Pour un jeu** : dans son profil, choisir l'écran virtuel comme **Source** (il est marqué
  « (virtuel) » dans les listes).
- **En ligne de commande** : `--virtual-create`, `--virtual-destroy`, `--virtual-modes`,
  `--virtual-add 1024x384@57.5`, `--virtual-remove 1280x240@60`, et `--display virtuel` pour le
  capturer.

Les modes sont dans `C:\VirtualDisplayDriver\vdd_settings.xml`, l'emplacement où le pilote les lit.
Le premier est celui d'un écran qui vient d'être créé. Le fichier écrit par SuperResScaler n'a pas
de fréquence globale : le pilote ajouterait chacune d'elles à toutes les tailles, ce qui créerait par
exemple un 1024x384 à 60 Hz que les bornes n'ont pas. Windows arrondit les fréquences : 57,5 Hz
apparaît en 58 (`SetResolution -f 58`). Chaque relecture des modes donne un nouveau `\\.\DISPLAYn`
à l'écran virtuel ; les profils, qui gardent l'identifiant du moniteur, n'en sont pas gênés.

**Relire les modes relance le pilote.** Pour relire ses modes, le pilote arrête son propre processus :
Windows y voit un plantage et le relance. Une mise à l'échelle de l'écran virtuel s'arrête alors,
avec les deux méthodes de capture (sur le PC de développement, la carte NVIDIA qui rend l'écran
virtuel est même réinitialisée). Surtout, après **cinq relectures en deux minutes environ**, Windows
arrête le périphérique (code 43, « Arrêté par Windows » sur la page). Il faut alors le détruire puis
le recréer, ou redémarrer le PC. Ajouter les modes en une fois (« Modes des bornes ») ne fait qu'une
relecture.

Le pilote est [Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver)
25.7.23 (licence MIT, signé par SignPath Foundation), **intégré à l'exe** sans modification : rien
d'autre à livrer. À la création, ses fichiers sont écrits dans un dossier temporaire après
vérification de leur SHA-256, installés, puis effacés : Windows en garde sa copie. Les originaux et
leur provenance sont dans `src/SuperResScaler/VirtualDisplayDriver/` (`SOURCE.txt`) ; leurs octets
ne doivent pas changer, le catalogue signé couvre l'INF à l'octet près (d'où le `.gitattributes`).
Détruire l'écran laisse le pilote dans le magasin de pilotes de Windows ; pour l'en retirer aussi
(en administrateur) : `pnputil /enum-drivers` pour trouver le `oemNN.inf` de `mttvdd.inf`, puis
`pnputil /delete-driver oemNN.inf /uninstall`.

### Raccourcis (modifiables dans Paramètres)

| Raccourci | Action |
| --- | --- |
| `Alt+Shift+A` | démarrer la mise à l'échelle de la fenêtre active / l'arrêter |
| `Alt+Shift+S` | shader suivant (enregistré dans le profil) |
| `Alt+Shift+C` | oublier et refaire la détection des bandes noires |

### Ligne de commande (front-end de borne : Attract-Mode, LaunchBox, RocketLauncher…)

Le mode ligne de commande attend la fenêtre, la met à l'échelle, et quitte quand elle se ferme :

```bash
SuperResScaler.exe --process game.exe --wait 60
SuperResScaler.exe --process Supermodel.exe --shader Nearest --autocrop
SuperResScaler.exe --title "Daytona" --crop 0,0,0,16 --delay 3
SuperResScaler.exe --process game.exe --mode fullscreen --aspect 4:3_letterbox
SuperResScaler.exe --process game.exe --aspect 4:3_crop --barrel --barrel-center 60
SuperResScaler.exe --process game.exe --resolution 1920x240@60
SuperResScaler.exe --process game.exe --mirror-x
SuperResScaler.exe --display 2 --monitor 1 --aspect 4:3
SuperResScaler.exe --display 2 --process game.exe --resolution 1920x240@60
SuperResScaler.exe --display virtuel --capture duplication --aspect 4:3
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
| **Écran de sortie** | *Auto* = l'écran où se trouve la fenêtre du jeu ; si tout un écran est capturé, un autre que celui-là |
| **Résolution** | mode d'affichage pendant la mise à l'échelle (ex. `1920x240@60`) — voir ci-dessous |
| **Miroir** | retourne toute la sortie pour un projecteur : X (`--mirror-x`) = gauche/droite, projecteur derrière un écran ; Y (`--mirror-y`) = haut/bas, projecteur par le dessus ; X et Y = image tournée de 180° |
| **Source** | la fenêtre du jeu, ou tout un écran (`--display`) — voir *Capturer un écran complet* |
| **Zone client uniquement** | ignore la barre de titre et les bordures |
| **Recadrage manuel** | pixels à retirer de chaque côté (barres d'outils d'émulateur, bords parasites…) |
| **Recadrage automatique** | retire les bandes noires (jeu 4:3 centré dans une fenêtre plus large). La zone détectée ne fait que grandir pour éviter les sauts lors des scènes sombres |
| **Capturer le curseur** | inclut le pointeur de la souris dans l'image (pas avec la duplication de bureau) |
| **Méthode de capture d'un écran** | *Auto*, `Windows.Graphics.Capture` ou duplication de bureau (`--capture`) — voir *Méthode de capture* |
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
`SuperResScaler.log`. Exemple de profil, tel que l'interface l'écrit :

```json
{
  "Profiles": [
    {
      "Name": "Daytona USA 2",
      "ProcessName": "Supermodel.exe",
      "WindowTitle": "Daytona",
      "AutoScale": true,
      "AutoScaleDelay": 1,
      "Shader": "Nearest",
      "Mode": "KeepAspect",
      "AspectRatio": "4:3",
      "Resolution": "1920x240@60"
    }
  ],
  "AutoScaleEnabled": true,
  "WatchWhileVisible": false,
  "WatchIntervalMs": 1000,
  "StartInTray": true,
  "CloseToTray": true
}
```

Les réglages absents d'un profil prennent leur valeur par défaut.

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
Il capture aussi l'écran entier où se trouve la mire, avec chaque méthode que ce Windows possède
(`Windows.Graphics.Capture` et la duplication de bureau), y retrouve celle-ci et lance une session
dont la sortie est sur cet écran ; de cette capture, seul le rectangle de la mire est enregistré.
Sous Windows 10 1809, la mire est découpée dans la capture de son écran par duplication, et les
sessions sur une fenêtre sont sautées (`SKIP`). Résultats dans `selftest.txt` + images PNG.

## Limites

- Les jeux en **plein écran exclusif** ne peuvent pas être capturés : utiliser le mode fenêtré
  ou « plein écran fenêtré / borderless » du jeu.
- La capture d'un **écran complet** ne garantit pas non plus le plein écran exclusif (selon la
  version de Windows et le jeu) : préférer le mode borderless. L'écran capturé doit faire partie du
  bureau étendu (pas désactivé, pas en duplication).
- Sous **Windows 10**, l'écran capturé par `Windows.Graphics.Capture` porte un liseré jaune
  imposé par le système (pas avec la duplication de bureau).
- Sous **Windows 10 1809**, seul un écran entier se capture (duplication de bureau, sans curseur).
- Une fenêtre **réduite** n'est pas capturée (l'overlay se masque jusqu'à sa restauration).
- Les **clics souris** traversent l'overlay et atteignent ce qui se trouve dessous aux mêmes
  coordonnées écran : ils ne sont pas remappés vers la position dans le jeu. Clavier et manettes
  ne sont pas concernés (le jeu garde le focus).
- La capture ajoute environ une image de latence (comme Magpie). Pendant la mise à l'échelle, le
  journal indique toutes les 10 s les images capturées par seconde et leur âge quand la boucle de
  rendu les prend (`Capture: … frames/s, frame age when picked up …`). Mesuré sur le PC de
  développement en capturant l'écran virtuel (640x480 ou 1920x1080, sortie en 640x480) : âge moyen
  inférieur à 1 ms, 17 ms au plus (une image à 60 Hz), et 11 à 15 % d'un cœur pour tout le
  processus. L'affichage avec VSync ajoute ensuite une image de l'écran de sortie. Avec la
  duplication de bureau, l'image est en moyenne 8 à 15 ms plus vieille quand elle est prise.
- Le contenu protégé (DRM) apparaît noir.

## Organisation du code

```
src/SuperResScaler/
  Program.cs                  point d'entrée (interface, ligne de commande, auto-test)
  App/ScalerController.cs     démarrage/arrêt, raccourcis, démarrage auto des profils
  App/HeadlessContext.cs      mode ligne de commande
  App/ProfileWatcher.cs       surveillance des processus et fenêtres (zone de notification)
  App/CommandLineOptions.cs   analyse des arguments
  App/SelfTest.cs             auto-test de bout en bout
  Engine/ScalingSession.cs    thread de rendu : capture -> shader -> présentation
  Engine/OverlayWindow.cs     fenêtre overlay (plein écran click-through ou prévisualisation)
  Engine/Geometry.cs          zone source, placement (formats du linuxloader, barillet)
  Native/DisplayModes.cs      modes d'affichage, changement et retour à la résolution d'origine
  Native/SetupApi.cs          création et suppression d'un périphérique racine (comme devcon)
  VirtualDisplay/             écran virtuel : pilote embarqué, création/destruction élevée, modes
  VirtualDisplayDriver/       pilote Virtual Display Driver signé, embarqué dans l'exe (SOURCE.txt)
  Engine/BlackBarDetector.cs  détection des bandes noires
  Engine/CaptureStatistics.cs images capturées par seconde et leur âge, dans le journal
  Capture/CaptureMethod.cs    choix de la méthode de capture selon la version de Windows
  Capture/CapturedFrame.cs    image capturée, source d'images commune aux deux méthodes
  Capture/FrameCapture.cs     Windows.Graphics.Capture (fenêtre ou écran, Windows 10 1903+)
  Capture/DuplicationCapture.cs  duplication de bureau DXGI (écran entier, Windows 10 1809)
  Capture/WinRTInterop.cs     ponts WinRT <-> D3D11
  Capture/ScreenSnapshot.cs   petite copie GDI d'un écran (vignette de l'écran virtuel)
  Rendering/Renderer.cs       device D3D11, compilation et exécution des shaders
  Rendering/SwapChainPresenter.cs  swap chain flip-model basse latence
  Config/                     options, profils, settings.json
  UI/MainForm.cs              fenêtre façon Magpie : navigation, pages, zone de notification
  UI/Pages/                   Accueil, Écran virtuel, profil (par défaut ou jeu), Paramètres, À propos
  UI/Controls.cs, Dialogs.cs  cartes de réglages, interrupteurs, raccourcis, Nouveau profil
  UI/DisplayIdentifier.cs     bouton Identifier : grand numéro sur chaque écran
  UI/VirtualDisplayThumbnail.cs  vignette de l'écran virtuel, en haut à droite des pages
  Shaders/                    shaders HLSL (copiés à côté de l'exécutable)
tests/SuperResScaler.Tests/   tests unitaires xUnit (placement, ratios, barillet, modes d'affichage, profils,
                              écran virtuel : modes, vdd_settings.xml, pilote embarqué intact, vignette ;
                              statistiques de capture)
```
