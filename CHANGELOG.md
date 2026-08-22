# Historique

## Identité Mobilis – Core

- la version 0.5.0 devient la base publique **Mobilis – Core** sous licence MIT ;
- les identifiants techniques historiques restent inchangés afin de conserver la compatibilité avec les mondes Independent Vehicles existants ;
- le futur mod **Mobilis** est développé séparément et n'est pas publié dans ce dépôt.

## 0.5.0

- capture transactionnelle des `BlockEntity` avant le retrait du moindre bloc ;
- conservation des inventaires et des correspondances d'identifiants de blocs/objets, y compris pour les coffres remplis ;
- rotation des données des blocs compatibles avec l'interface publique `IRotatable` ;
- restauration préchargée des entités de bloc avant leur réapparition dans le monde et retour complet à l'état mobile si une pose échoue ;
- refus d'activer un véhicule lorsqu'un inventaire embarqué est encore ouvert, lorsqu'une classe de données n'est pas enregistrée ou lorsque les données dépassent les limites de sécurité ;
- rendu mobile générique des maillages fournis par `BlockEntity.OnTesselation`, avec repli sur le maillage normal du bloc ;
- conservation des boîtes de collision dynamiques et fusion de la lumière des blocs embarqués ;
- compatibilité visée pour les coffres, lanternes, blocs ciselés et blocs à données de mods qui respectent le contrat public de persistance de Vintage Story ;
- aucun patch Harmony et aucune méthode interne interceptée.

## 0.4.4

- retrait de la réconciliation `TryMount` côté client de la 0.4.3, identifiée comme la cause directe des sauts visuels à plusieurs centaines de milliers de blocs ;
- refus de toute ancre client non finie, située dans une autre dimension ou distante de plus de 16 blocs de la position courante ;
- conservation de la position actuelle par `SeatPosition` lorsque l'entité mobile n'est pas encore dans le même repère client ;
- descente serveur automatique et rematérialisation normale si Vanilla ne restaure pas le siège local confirmé sous 1,5 seconde ;
- aucune modification de la colle, des collisions, des pentes ou de la transition de rematérialisation et aucun patch Harmony.

## 0.4.3

- réconciliation automatique du siège client avec l'attachement confirmé par le serveur lorsque Vanilla perd `mountedOn` après une descente/réactivation rapide ;
- conservation du dernier état d'attachement reçu pendant l'écran de connexion jusqu'à la création du joueur local ;
- ajout d'un délai serveur d'une seconde après la descente avant de pouvoir réactiver le siège ;
- suppression du cas où le client marche librement tandis que le serveur le corrige encore vers le véhicule, cause commune du véhicule bloqué et du tremblement observé.
- ajout de `/iv dismount` comme sortie de secours autoritaire si l'état visuel du client redevient incohérent.

## 0.4.2

- correction du crash à la connexion lorsqu'un état d'attachement sauvegardé arrive avant la création de `World.Player` côté client ;
- sécurisation de tous les accès au joueur local pendant la montée, la descente et les ticks de connexion ;
- l'état reçu trop tôt est ignoré puis redemandé automatiquement par le mécanisme de resynchronisation 0.4.1.

## 0.4.1

- correction d'une course réseau où le jeton d'attachement pouvait arriver avant l'état Vanilla `mountedOn`, puis être effacé au tick suivant ;
- ajout d'une demande de resynchronisation limitée en fréquence lorsqu'un client est assis sans jeton ;
- validation serveur de cette demande contre le passager et le siège réellement montés, sans accepter de commande ni de position ;
- restauration du déplacement, de la rotation et de la descente après cette resynchronisation.

## 0.4.0

- remplacement des commandes de monture par des paquets directionnels propres au mod, séquencés et validés par un jeton d'attachement serveur ;
- ajout de `VehicleRiderAttachmentSystem` et d'une ancre locale fixe transformée avec la structure ;
- conservation d'un siège Vanilla virtuel uniquement pour la pose assise et la physique joueur, sans contrôle ni position de monture envoyée par le client ;
- réapplication autoritaire de l'ancre sur le serveur et diagnostic limité en fréquence des corrections Vanilla anormalement grandes ;
- orientation du corps fixée au siège tout en laissant le regard libre ;
- libération sûre d'un passager devenu incohérent et rematérialisation par la transition existante ;
- aucun patch Harmony ni interception de méthode interne.

## 0.3.5

- ajout de quatre orientations persistantes au siège de contrôle ;
- l'avant du véhicule est désormais celui du siège et ne dépend plus de l'angle du joueur après une remontée ;
- capture des offsets et des blocs orientables dans le repère local du véhicule ;
- correction du signe de rotation des blocs orientables lors de la rematérialisation ;
- migration automatique des anciens sièges sans orientation à leur première activation ;
- affichage explicite de `Independent Vehicles 0.3.5` dans le chat à l'activation afin de vérifier la version réellement chargée ;
- conservation des protections 0.3.4 contre la concurrence entre la monture prédictive et le serveur.

## 0.3.4

- correction du conflit entre le déplacement autoritaire du serveur et le mode de monture prédictive de Vintage Story ;
- siège exposé comme passager non prédictif afin que le client local interpole les positions reçues au lieu de les ignorer ;
- conservation des commandes Z/W, S, A et D dans les contrôles du siège, toujours interprétées uniquement par le serveur ;
- retrait de la téléportation explicite pendant la rematérialisation : le joueur reste à la dernière position physique du siège ;
- position assise relevée de 0,58 à 0,64 bloc pour rester juste au-dessus de la boîte de collision du siège lorsqu’il redevient solide.

## 0.3.3

- correction du vecteur avant pour suivre exactement la convention vanilla : Z/W ne sont plus inversées ;
- conservation de la convention vanilla pour A/D, qui tourne à gauche/droite dans le bon sens ;
- alignement initial du regard local sur le cap du véhicule lors de la montée, indépendamment de la direction regardée avant le clic ;
- position du siège recalculée dans une instance stable, comme pour les montures vanilla, sans réintroduire de physique client du véhicule.

## 0.3.2

- retrait complet de la prédiction `IPhysicsTickable` côté client qui envoyait la structure à des coordonnées divergentes du serveur ;
- retour au déplacement autoritaire côté serveur de la version 0.3.0, avec conservation du franchissement des marches d’un bloc ;
- suppression de la réémission périodique de `mountedOn` et des événements manuels de monture ;
- libération serveur d’un passager devenu incohérent afin qu’un joueur à pied ne puisse plus conserver les commandes ;
- verrou de cycle de vie empêchant une même entité mobile de déposer ses blocs deux fois ;
- abandon explicite d’une entité dont l’activation échoue, sans rematérialisation parasite ;
- restauration des blocs déjà posés si une exception interrompt la rematérialisation.

## 0.3.1

- déplacement raccordé au cycle `IPhysicsTickable` utilisé par les montures vanilla, côté client local et serveur ;
- position du siège conservée dans une instance stable afin d’éviter la dérive visuelle du passager pendant les marches ;
- resynchronisation périodique et légère de l’état monté tant que le serveur confirme le même siège ;
- arrêt des commandes si le passager et le siège ne se reconnaissent plus mutuellement ;
- émission des événements vanilla de montée et de descente.

## 0.3.0

- ajout d’un modèle de déplacement terrestre avec montée et descente automatiques d’une marche exactement ;
- vérification de l’espace au-dessus avant une montée ;
- arrêt devant un obstacle de deux blocs ou davantage ;
- arrêt au bord lorsque aucun sol n’existe au niveau courant ou un bloc plus bas.

## 0.2.1

- collision contre le décor calculée avec le volume orienté complet des blocs, y compris pendant la rotation ;
- mouvement découpé en sous-étapes afin d’éviter de traverser un obstacle lors d’un tick long ;
- le siège est maintenant déclaré non opaque et ne masque plus la face du bloc placé dessous ;
- ajout du libellé de la structure mobile ;
- ajout de `/iv recover` pour rematérialiser en sécurité une structure inoccupée proche.

## 0.2.0

- la colle fonctionne maintenant comme un pinceau : clic droit maintenu puis passage du viseur d’un bloc à son voisin ;
- le groupe collé visé est surligné en cyan, avec le bloc courant en orange ;
- accroupissement + pinceau retire les liaisons ;
- les opérations de colle sont validées par le serveur et ne produisent plus une ligne de chat par liaison.

## 0.1.1

- le siège laisse désormais l’outil de colle enregistrer une liaison au lieu de tenter immédiatement l’activation ;
- correction des domaines des formes et sons vanilla ;
- le rendu de la structure mobile ignore proprement la passe d’ombres, ce qui évite le crash « Already a different shader in use ».

## 0.1.0

- premier prototype fixe ↔ mobile.
