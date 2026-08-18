# Architecture du prototype

Cette base est volontairement petite et indépendante : elle utilise uniquement l’API publique de Vintage Story et ne dépend d’aucun mod de véhicules.

## Machine à deux états

### Fixe

La plateforme existe sous forme de vrais blocs dans le monde. Vintage Story fournit donc naturellement les collisions, la marche et les interactions normales. Aucune entité mobile ne subsiste et aucune vélocité n’est conservée.

### Mobile

Les blocs de la composante collée sont capturés dans un `VehicleSnapshot`, retirés du monde, puis rendus par une seule `EntityVehicleStructure`. Pour chaque entité de bloc, le snapshot contient le nom de sa classe enregistrée, son `TreeAttribute`, les correspondances d'identifiants de blocs et d'objets, ses boîtes de collision dynamiques et sa lumière. Les offsets entiers sont convertis dans le repère local du siège orienté : son dossier définit l'avant et cette orientation survit aux cycles fixe ↔ mobile. Le serveur est l’unique autorité sur la position, la rotation et les collisions avec le décor.

La capture suit le contrat public employé par les schémas de Vintage Story : `ToTreeAttributes`, `OnStoreCollectibleMappings`, puis, au retour, `FromTreeAttributes` avant `Initialize` et `OnLoadCollectibleMappings`. Les données qui implémentent `IRotatable` reçoivent également `OnTransformed`. Le bloc et ses données sont validés avant le retrait du premier bloc. Lors de la rematérialisation, tous les emplacements et toutes les données sont prévalidés ; si une restauration échoue, les blocs déjà posés sont retirés et le snapshot mobile reste l'unique copie.

Côté client, une instance temporaire de l'entité de bloc fournit son maillage via `OnTesselation`. Si elle ne sait pas produire un maillage hors du monde, le rendu revient au maillage normal du bloc sans compromettre les données sauvegardées. Les inventaires ne sont pas instanciés comme interfaces utilisables pendant le déplacement : ils sont fermés avant l'activation et redeviennent accessibles seulement après la descente.

`VehicleRiderAttachmentSystem` sépare le conducteur de la physique de la structure. Le joueur utilise le contrat public `IMountableSeat`, mais le siège est virtuel (`Entity == null`) : Vanilla sait que le joueur est assis sans considérer la structure comme une monture dont le client piloterait la position. `SeatPosition` transforme à chaque lecture l'ancre locale fixe `(0, 0.64, 0)` avec la position et le yaw autoritaires de la structure. Les paquets du mod ne contiennent que les quatre commandes directionnelles, la demande de descente, une séquence et un jeton d'attachement généré par le serveur. Ils ne contiennent jamais de coordonnées monde. Le corps du conducteur est contraint au yaw du siège tandis que le regard reste libre.

Vintage Story 1.22.6 envoie encore automatiquement un paquet de position du joueur monté toutes les quatre étapes physiques. L'appel part de la méthode interne `Vintagestory.GameContent.EntityBehaviorPlayerPhysics.OnRenderFrame`, qui invoque la méthode publique `ICoreClientNetworkAPI.SendPlayerPositionPacket()`. Côté serveur, la méthode interne `Vintagestory.Server.Systems.ServerUdpNetwork.HandlePlayerPosition` applique ensuite le paquet à `EntityPlayer.Pos` avant d'appeler la physique distante. L'API publique permet d'émettre un paquet, mais ne fournit aucun événement annulable avant cet envoi ni avant son application côté serveur.

La solution sans patch garantit donc qu'au moment de cet envoi la physique Vanilla lit exactement la même `SeatPosition` calculée, puis le serveur réapplique l'ancre après le traitement réseau. Dans la version 1.22.6, les anciennes propriétés `SidedPos` et `ServerPos` sont obsolètes et renvoient la même instance que `Pos` : mettre `Pos` à jour couvre donc bien les trois noms sans écriture interne supplémentaire. Un écart supérieur à quatre blocs est corrigé et journalisé, un paquet du mod obsolète, dupliqué ou appartenant à un ancien montage est refusé, et une commande sans battement récent arrête le véhicule.

Le paquet d'état d'attachement peut arriver avant la réplication Vanilla de `mountedOn`. Si le joueur local n'existe pas encore, le dernier état reçu est conservé jusqu'à la fin de la connexion. Si le client se retrouve assis sans jeton validé, il demande périodiquement un nouvel état au serveur. Inversement, le mod ne force jamais un `TryMount` côté client : une entité interpolée peut momentanément employer le repère décalé du client dans les mondes éloignés de l'origine. Si le serveur confirme encore l'attachement mais que Vanilla laisse le client à pied pendant plus de 1,5 seconde, le client demande une descente serveur validée. Le véhicule s'arrête alors et utilise sa rematérialisation normale, sans correction de position locale.

Intercepter `EntityBehaviorPlayerPhysics.OnRenderFrame`, `ICoreClientNetworkAPI.SendPlayerPositionPacket()` ou `ServerUdpNetwork.HandlePlayerPosition` avec Harmony supprimerait le paquet ou son application, mais l'une des deux extrémités resterait alors liée à des classes internes, à leur signature et à leur ordre d'exécution. Ce serait une zone très sensible aux mises à jour. La version 0.5.0 ne réalise aucun de ces patchs.

Le modèle terrestre conserve la structure horizontale. À chaque sous-étape, il accepte d’abord la pose au même niveau, puis essaie au maximum une case plus haut si un obstacle bloque l’avant, ou une case plus bas si le sol descend. Toute pose doit être libre et posséder un support ; un mur de deux blocs et un vide plus profond restent infranchissables.

Quand le joueur descend, la position est arrondie à la grille et l’angle au quart de tour le plus proche. L’entité est supprimée seulement après la remise en place de tous les vrais blocs et des liaisons de colle.

## Composants

- `GlueRegistry` : graphe non orienté de liaisons entre blocs partageant une face ;
- `GlueBrushPacket` : requêtes client validées par le serveur pour peindre/retirer une arête et demander la composante à surligner ;
- `VehicleSnapshot` : codes des blocs, offsets relatifs, données sérialisées des entités de bloc, collisions, lumière et liaisons locales ;
- `IndependentVehiclesSystem` : validation et transitions transactionnelles fixe ↔ mobile ;
- `EntityVehicleStructure` : état du mouvement horizontal sans inertie et siège unique ;
- `VehicleRiderAttachmentSystem` : ancre locale du conducteur, commandes séquencées, validation serveur et descente ;
- `VehicleMaterializationGuard` : verrou monotone du passage entité mobile vers blocs fixes ;
- `VehicleCollisionMath` : test d’intersection entre les volumes orientés des blocs mobiles et les boîtes de collision du monde ;
- `VehicleStructureRenderer` : fusion des maillages de blocs et des maillages publics produits par leurs entités de bloc côté client ;
- un bloc `VehicleControlSeat` et un item `StructureGlue`.

Il n’existe pas un contrôleur différent par type de véhicule. Les futurs bateaux, chariots ou aéronefs devront réutiliser le même snapshot et les mêmes transitions ; seule leur règle de mouvement changera.

## Invariants de sécurité

- maximum 64 blocs et volume maximal 9×9×5 dans la version 0.5.0 ;
- aucune capture de liquide ou d’air ;
- au maximum 256 Kio par entité de bloc et 1 Mio de données d'entités de bloc par véhicule ;
- aucun inventaire embarqué ne peut rester ouvert pendant l'activation ;
- retrait des blocs et apparition de l’entité traités comme une transaction avec restauration intégrale des entités de bloc en cas d’échec ;
- aucune rematérialisation ne peut écraser un bloc existant ;
- une entité ne peut commencer qu’une seule rematérialisation et, une fois validée, ne peut plus déposer ses blocs ;
- la translation et la rotation sont découpées en sous-étapes et refusées dès qu’un volume mobile pénètre une boîte de collision du monde ;
- mouvement appliqué directement à la position, puis vecteur de vélocité remis à zéro à chaque tick ;
- arrêt immédiat et rematérialisation déclenchés par la descente du siège.
- aucune téléportation explicite du joueur pendant la descente : sa dernière position de siège devient directement sa position debout sur la structure solide.
- aucun paquet du mod ne peut imposer une position de joueur ou de structure ; le serveur ne lit que des commandes validées pour le conducteur réellement attaché.

La commande de secours `/iv dismount` appelle la descente normale uniquement lorsque le serveur confirme que son auteur conduit un siège du mod. `/iv recover` ne cible que la structure mobile inoccupée la plus proche dans un rayon de 16 blocs. Elle réutilise exactement la transition transactionnelle normale et laisse l’entité intacte si aucune pose sûre n’existe.

La version 0.5.0 couvre les blocs à données qui savent se sauvegarder et se restaurer par le contrat public de Vintage Story. Elle ne peut pas garantir automatiquement la cohérence d'un système externe au véhicule : une entité de bloc qui conserve des références privées vers un réseau, un autre bloc ou une structure multibloc reste expérimentale et peut être refusée si sa restauration publique échoue.
