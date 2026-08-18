using IndependentVehicles.Core;
using IndependentVehicles.Glue;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

LocalPos start = new(2, 1, -3);
Assert(start.RotateQuarterTurns(1) == new LocalPos(-3, 1, -2), "Rotation 90° incorrecte.");
Assert(start.RotateQuarterTurns(2) == new LocalPos(-2, 1, 3), "Rotation 180° incorrecte.");
Assert(start.RotateQuarterTurns(3) == new LocalPos(3, 1, 2), "Rotation 270° incorrecte.");
Assert(start.RotateQuarterTurns(4) == start, "Quatre quarts de tour doivent rendre la position initiale.");
Assert(QuarterTurn.FromYaw(MathF.PI * 2) == 0, "La normalisation d’un tour complet a échoué.");
for (int headingTurns = 0; headingTurns < 4; headingTurns++)
{
    LocalPos local = start.RotateQuarterTurns(-headingTurns);
    Assert(local.RotateQuarterTurns(headingTurns) == start,
        $"La conversion monde ↔ véhicule a échoué pour le cap {headingTurns}.");
}

(double forwardX0, double forwardZ0) = VehicleControlMath.ForwardVector(0);
Assert(Math.Abs(forwardX0) < 0.000001 && Math.Abs(forwardZ0 + 1) < 0.000001,
    "À yaw zéro, avancer doit suivre l'avant vanilla vers Z négatif.");
(double forwardX90, double forwardZ90) = VehicleControlMath.ForwardVector(MathF.PI / 2);
Assert(Math.Abs(forwardX90 + 1) < 0.000001 && Math.Abs(forwardZ90) < 0.000001,
    "À yaw 90°, avancer doit suivre l'avant vanilla vers X négatif.");
Assert(VehicleControlMath.DriveInput(true, false) == 1 && VehicleControlMath.DriveInput(false, true) == -1,
    "Les commandes avant/arrière sont inversées.");
Assert(VehicleControlMath.TurnInput(true, false) == 1 && VehicleControlMath.TurnInput(false, true) == -1,
    "Les commandes gauche/droite ne suivent pas la convention vanilla.");

VehicleRiderAnchor riderNorth = VehicleRiderMath.LocalToWorld(
    10, 20, 30, 0, 0, VehicleRiderMath.DefaultLocalY, -1);
Assert(Math.Abs(riderNorth.X - 10) < 0.000001 && Math.Abs(riderNorth.Y - 20.64) < 0.000001 && Math.Abs(riderNorth.Z - 29) < 0.000001,
    "L’ancre locale du conducteur est incorrecte à yaw zéro.");
VehicleRiderAnchor riderWest = VehicleRiderMath.LocalToWorld(
    10, 20, 30, MathF.PI / 2, 0, VehicleRiderMath.DefaultLocalY, -1);
Assert(Math.Abs(riderWest.X - 9) < 0.000001 && Math.Abs(riderWest.Y - 20.64) < 0.000001 && Math.Abs(riderWest.Z - 30) < 0.000001,
    "L’ancre locale du conducteur ne suit pas la rotation de la structure.");
Assert(VehicleRiderMath.IsPlausibleCorrection(10, 20, 30, riderNorth, 2),
    "Une correction de siège voisine a été refusée.");
Assert(!VehicleRiderMath.IsPlausibleCorrection(
        511948,
        4,
        512031,
        new VehicleRiderAnchor(-141174, 3, -141114),
        16),
    "Un mélange de repères client distant aurait dû être refusé.");
Assert(!VehicleRiderMath.IsPlausibleCorrection(double.NaN, 4, 5, riderNorth, 16),
    "Une position non finie aurait dû être refusée.");

var riderInput = new VehicleRiderInputState();
Assert(riderInput.Accept(1, VehicleRiderInputBits.Forward | 0x4000, 1000),
    "Le premier paquet de commandes conducteur a été refusé.");
Assert(riderInput.FreshControlBits(1100, 600) == VehicleRiderInputBits.Forward,
    "Les bits de commandes inconnus ne sont pas filtrés.");
Assert(!riderInput.Accept(1, VehicleRiderInputBits.Backward, 1200),
    "Un paquet conducteur dupliqué ne doit pas être accepté.");
Assert(riderInput.FreshControlBits(1701, 600) == 0,
    "Une commande conducteur périmée doit arrêter la structure.");
Assert(riderInput.Accept(2, VehicleRiderInputBits.Left, 1800),
    "La séquence conducteur suivante a été refusée.");
Assert(riderInput.FreshControlBits(1800, 600) == VehicleRiderInputBits.Left,
    "La dernière commande conducteur valide n’a pas été conservée.");

bool TouchesAtFace = VehicleCollisionMath.IntersectsOrientedPrismWithAabb(
    0.5, 0, 0.5, 0.5, 1, 0.5, 0,
    1, 0, 0, 2, 1, 1);
Assert(!TouchesAtFace, "Deux volumes qui se touchent sans se chevaucher ne doivent pas entrer en collision.");

bool PenetratesWall = VehicleCollisionMath.IntersectsOrientedPrismWithAabb(
    0.51, 0, 0.5, 0.5, 1, 0.5, 0,
    1, 0, 0, 2, 1, 1);
Assert(PenetratesWall, "Une faible pénétration dans un mur doit être détectée.");

double fortyFiveDegrees = Math.PI / 4;
bool RotatedCornerHits = VehicleCollisionMath.IntersectsOrientedPrismWithAabb(
    0, 0, 0, 0.5, 1, 0.5, fortyFiveDegrees,
    0.69, 0, -0.1, 1.69, 1, 0.9);
Assert(RotatedCornerHits, "Le coin d’un bloc tourné doit être détecté contre le mur.");

bool RestsOnGround = VehicleCollisionMath.IntersectsOrientedPrismWithAabb(
    0.5, 1, 0.5, 0.5, 2, 0.5, 0,
    0, 0, 0, 1, 1, 1);
Assert(!RestsOnGround, "Un bloc posé sur le sol ne doit pas être considéré comme pénétrant le sol.");

GridPos a = new(0, 0, 0);
GridPos b = new(1, 0, 0);
GridPos c = new(1, 0, 1);
GridPos isolated = new(10, 0, 10);
Assert(a.IsFaceAdjacent(b), "Deux voisins par face doivent être adjacents.");
Assert(!a.IsFaceAdjacent(c), "Une diagonale ne doit pas être adjacente.");
Assert(new GlueBond(a, b) == new GlueBond(b, a), "Une liaison doit être indépendante de l’ordre des blocs.");

var registry = new GlueRegistry();
Assert(registry.Add(a, b), "La première liaison n’a pas été ajoutée.");
Assert(registry.Add(b, c), "La deuxième liaison n’a pas été ajoutée.");
HashSet<GridPos> component = registry.GetConnectedComponent(a, 64);
Assert(component.SetEquals([a, b, c]), "La composante collée est incorrecte.");
Assert(registry.GetConnectedComponent(isolated, 64).SetEquals([isolated]), "Un bloc isolé doit rester seul.");
Assert(registry.Remove(a, b), "La suppression explicite doit retirer la liaison.");
Assert(registry.GetConnectedComponent(a, 64).SetEquals([a]), "La liaison retirée reste présente dans le graphe.");
Assert(registry.Add(a, b), "Le pinceau doit pouvoir ajouter une liaison explicite.");
Assert(!registry.Add(a, b), "Repeindre la même liaison doit être idempotent.");
Assert(registry.Remove(a, b), "Le pinceau en mode retrait doit supprimer la liaison.");
Assert(!registry.Remove(a, b), "Retirer deux fois la même liaison doit être idempotent.");

var materialization = new VehicleMaterializationGuard();
Assert(materialization.TryBegin(), "La première rematérialisation doit pouvoir commencer.");
Assert(!materialization.TryBegin(), "Deux rematérialisations simultanées doivent être refusées.");
Assert(materialization.TryCancel(), "Une rematérialisation non validée doit pouvoir être annulée.");
Assert(materialization.TryBegin(), "Une rematérialisation annulée doit pouvoir être retentée.");
Assert(materialization.TryComplete(), "La rematérialisation en cours doit pouvoir être validée.");
Assert(materialization.Phase == VehicleMaterializationPhase.Materialized, "La phase terminale matérialisée est incorrecte.");
Assert(!materialization.TryBegin(), "Une structure déjà matérialisée ne doit jamais pouvoir déposer ses blocs une seconde fois.");

var discarded = new VehicleMaterializationGuard();
Assert(discarded.TryDiscard(), "Une entité en attente doit pouvoir être abandonnée.");
Assert(!discarded.TryBegin(), "Une entité abandonnée pendant l'activation ne doit pas se rematérialiser.");
Assert(!materialization.TryDiscard(), "Une structure déjà matérialisée ne doit pas pouvoir changer d'état terminal.");

var snapshot = new VehicleSnapshot
{
    SchemaVersion = 2,
    OriginalController = new GridPos(12, 34, -5, 2),
    CollectibleMappingSeed = 12345,
    BlockIdMappings = new Dictionary<int, string> { [42] = "game:planks-oak" },
    ItemIdMappings = new Dictionary<int, string> { [7] = "game:gear-rusty" },
    Blocks =
    [
        new VehicleBlockSnapshot
        {
            Offset = new LocalPos(0, 0, 0),
            BlockCode = "independentvehicles:vehiclecontrolseat",
            BlockEntity = VehicleBlockEntitySnapshot.FromBytes("GenericContainer", [1, 2, 3, 4]),
            CollisionBoxes =
            [
                new VehicleCuboidSnapshot
                {
                    X1 = 0.1f,
                    Y1 = 0,
                    Z1 = 0.2f,
                    X2 = 0.9f,
                    Y2 = 0.8f,
                    Z2 = 0.7f
                }
            ],
            LightHsv = [7, 4, 18]
        },
        new VehicleBlockSnapshot { Offset = new LocalPos(1, 0, 0), BlockCode = "game:planks-oak" }
    ],
    Bonds = [new LocalBond(new LocalPos(0, 0, 0), new LocalPos(1, 0, 0))]
};
VehicleSnapshot? restored = VehicleJson.Deserialize<VehicleSnapshot>(VehicleJson.Serialize(snapshot));
if (restored is null) throw new InvalidOperationException("Le snapshot sérialisé n’a pas pu être relu.");
Assert(restored.OriginalController == snapshot.OriginalController, "L’ancre du snapshot a changé.");
Assert(restored.Blocks.Count == 2 && restored.Bonds.Count == 1, "Le contenu du snapshot a changé.");
Assert(restored.SchemaVersion == 2 && restored.CollectibleMappingSeed == 12345,
    "La version ou la graine du snapshot à données a changé.");
Assert(restored.BlockIdMappings[42] == "game:planks-oak" && restored.ItemIdMappings[7] == "game:gear-rusty",
    "Les correspondances d’identifiants du coffre ont changé.");
Assert(restored.Blocks[0].BlockEntity?.DecodeTreeData().SequenceEqual(new byte[] { 1, 2, 3, 4 }) == true,
    "Les données binaires de la BlockEntity ont changé.");
Assert(restored.Blocks[0].CollisionBoxes.Count == 1 && restored.Blocks[0].LightHsv?.SequenceEqual(new byte[] { 7, 4, 18 }) == true,
    "Les collisions ou la lumière du bloc à données ont changé.");
VehicleCuboidSnapshot rotatedBox = restored.Blocks[0].CollisionBoxes[0].RotateQuarterTurns(1);
Assert(Math.Abs(rotatedBox.X1 - 0.2f) < 0.000001 &&
       Math.Abs(rotatedBox.X2 - 0.7f) < 0.000001 &&
       Math.Abs(rotatedBox.Z1 - 0.1f) < 0.000001 &&
       Math.Abs(rotatedBox.Z2 - 0.9f) < 0.000001,
    "La collision dynamique n’a pas suivi le quart de tour du véhicule.");

Console.WriteLine("Tous les tests du cœur indépendant ont réussi.");
