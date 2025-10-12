# Tiroir Collaboratif – Starter Unity

## Contenu
- `Assets/Scripts/` : scripts C# de base (multitouch, gestes, pièces/snap, étapes).
- `Assets/Resources/Assemblies/stool.json` : exemple d'assemblage (tabouret 3 pièces).

## Version
- Unity 2022 LTS+ (URP ou Built-In).

## Mise en place rapide
1. Créez une scène `Main` avec :
   - Camera, Directional Light.
   - Canvas (Screen Space - Overlay) et un `Image` rond désactivé nommé `TouchPoint`.
   - Ajoutez `MultiTouchManager` sur un GameObject vide.
   - Ajoutez `TouchVisualizer` et référençez le prefab UI `TouchPoint`.
   - Placez les GameObjects `Seat`, `Leg_A`, `Leg_B` avec un `Collider` + `PartController` (renseigner `compatibleSnapTag`).
   - Créez des objets vides `SnapPoint` (tag "SnapPoint") avec composant `SnapPoint` et `snapTag` correspondant.
   - Ajoutez `AssemblyManager` et assignez un matériau `highlightMat`.

2. Vérifiez `Resources/Assemblies/stool.json` (charge au Start).

3. En Play, testez avec la souris (un doigt) ou sur un écran tactile (multi-doigts).

## Réseau (option)
- Activez `USE_NETCODE` dans **Project Settings → Player → Scripting Define Symbols**.
- Installez *Unity Netcode for GameObjects* via le Package Manager.

## XR (option)
- Ajoutez le XR Plugin Management et une seconde scène dédiée.

## Licence
Starter destiné à un projet pédagogique. Utilisez/étendez librement.
