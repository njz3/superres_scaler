# Shaders

Chaque fichier `.hlsl` de ce dossier apparaît dans la liste des shaders (le nom affiché
est le nom du fichier). Les shaders sont compilés au lancement de la mise à l'échelle :
on peut les modifier ou en ajouter sans recompiler l'application
(`Alt+Shift+S` passe au shader suivant pendant la mise à l'échelle).

## Contrat

- un seul pixel shader, point d'entrée `main`, compilé en `ps_5_0` ;
- `#include "Common.hlsli"` fournit les déclarations ci-dessous ;
- le shader est exécuté une fois par pixel du rectangle de destination.

| Nom | Type | Contenu |
| --- | --- | --- |
| `Source` | `Texture2D<float4>` (t0) | image capturée, déjà recadrée |
| `PointSampler` | `SamplerState` (s0) | plus proche voisin, bords clampés |
| `LinearSampler` | `SamplerState` (s1) | bilinéaire, bords clampés |
| `SourceSize` / `InvSourceSize` | `float2` | taille de l'image source en pixels (et inverse) |
| `OutputSize` / `InvOutputSize` | `float2` | taille du rectangle de destination (et inverse) |
| `OutputOffset` | `float2` | coin haut-gauche de la destination sur l'écran |
| `Time` | `float` | secondes depuis le début de la mise à l'échelle |
| `FrameCount` | `uint` | numéro d'image |
| `input.TexCoord` | `float2` | 0..1 sur la destination, au centre des pixels |
| `Texel(int2 p)` | fonction | lit un pixel source (bords clampés) |

## Exemple

```hlsl
#include "Common.hlsli"

float4 main(PSInput input) : SV_TARGET
{
    float2 texel = input.TexCoord * SourceSize;   // position en pixels source
    return Texel(int2(floor(texel)));              // plus proche voisin
}
```

En cas d'erreur de compilation, le message est écrit dans `SuperResScaler.log` et le
shader précédent (ou un plus-proche-voisin intégré) reste actif.
