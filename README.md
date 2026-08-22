# Mobilis – Core

Mobilis – Core est la base open source de notre système de véhicules pour Vintage Story. Cette implémentation indépendante est conçue de zéro : elle ne dépend pas de VSA Replica et n’en reprend pas le code.

Le dépôt est volontairement public sous licence MIT. Vous pouvez étudier, modifier, redistribuer ou utiliser cette base pour créer votre propre mod, à condition de conserver la notice de licence. Le futur mod **Mobilis** est un projet distinct développé dans un dépôt privé ; son contenu propre ne fait pas partie de Mobilis – Core.

Le prototype valide une seule boucle : sélectionner des liaisons entre blocs vanilla, transformer la composante collée en une entité mobile unique, la conduire depuis un siège, puis la rematérialiser en vrais blocs fixes à la descente.

## Périmètre de la version 0.5.0

- 2 à 64 blocs, dans un volume maximal de 9×9×5 ;
- un seul siège de contrôle fourni par le mod ;
- attachement conducteur virtuel à une ancre locale du siège, sans position client dans les paquets du mod ;
- un outil de colle fourni par le mod ;
- déplacement horizontal et rotation ;
- montée et descente automatiques de marches d’un bloc, avec arrêt devant deux blocs ou un précipice ;
- arrêt sans inertie et retour à de vrais blocs sur la grille ;
- collision vérifiée sur le volume orienté complet de chaque bloc mobile ;
- sauvegarde et restauration des entités de bloc qui respectent le contrat public de persistance de Vintage Story ;
- inventaire des coffres conservé, que le coffre soit vide ou rempli ;
- rendu mobile générique des coffres, lanternes, blocs ciselés et autres entités de bloc via leur tessellation publique ;
- boîtes de collision dynamiques et lumière émises par les blocs mémorisées dans la structure mobile.

Le prototype ne simule ni portance, ni carburant, ni roues, ni flottabilité, ni poids.

Les coffres doivent être fermés avant l'activation et leur contenu est volontairement inaccessible pendant le déplacement : ils redeviennent interactifs après la descente. Les blocs liés à un réseau extérieur ou à plusieurs positions (mécanismes, multiblocs, conduites, etc.) restent expérimentaux, car leur état peut dépendre d'autres blocs qui ne font pas partie du véhicule. Une entité de bloc non enregistrée, invalide ou trop volumineuse fait refuser l'activation avant le retrait du moindre bloc.

## Construire et essayer

1. Installez Vintage Story 1.22.6 et le SDK .NET utilisé par cette installation (ici .NET 10).
2. Exécutez `dotnet build src/IndependentVehicles.csproj`.
3. Exécutez `./build.ps1` puis copiez `dist/Mobilis-Core-0.5.0.zip` dans le dossier `Mods` de Vintage Story.
4. En créatif, prenez « Colle de structure » et « Siège de contrôle de véhicule ».
5. Construisez une petite plateforme de planches et posez le siège dessus.
6. Prenez la colle : le groupe collé sous le viseur apparaît en cyan et le bloc visé en orange.
7. Maintenez le clic droit, puis balayez les blocs voisins avec le viseur. Chaque passage entre deux blocs partageant une face ajoute automatiquement la liaison, siège compris. Maintenir la touche d’accroupissement pendant le balayage retire les liaisons.
8. Rangez la colle, puis faites un clic droit sur le siège pour activer la plateforme.
9. Utilisez les touches configurées pour avancer/reculer et gauche/droite. Accroupissez-vous pour descendre et figer la plateforme.

Le dossier `Mods` ne doit contenir qu'une version de Mobilis – Core. Le chat doit afficher `Mobilis - Core 0.5.0` lors de l'activation. Le dossier du siège indique l'avant du véhicule et cette direction reste mémorisée entre deux activations.

Pour le premier essai de la 0.5.0, utilisez d'abord un coffre vide, puis un coffre contenant un objet sans valeur, et conservez une sauvegarde du monde. Testez ensuite une rotation et une descente avant d'y placer des ressources importantes.

En cas de désynchronisation visuelle exceptionnelle où le serveur considère encore le joueur comme conducteur, `/iv dismount` force une descente serveur sûre. `/iv recover` reste réservé à une structure mobile inoccupée.

Si une structure mobile devait exceptionnellement perdre son conducteur sans se rematérialiser, placez-vous à moins de 16 blocs et utilisez `/iv recover`. La commande ignore les véhicules encore occupés et ne supprime rien lorsqu’aucun emplacement sûr n’est disponible.

La rotation est ramenée au quart de tour le plus proche lors de la rematérialisation, car les blocs fixes de Vintage Story vivent sur une grille entière.

L’architecture et ses invariants de sécurité sont décrits dans `docs/ARCHITECTURE.md`.
