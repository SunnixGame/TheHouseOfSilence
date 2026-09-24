using System.Collections.Generic;
using HouseOfSilence.Core;
using HouseOfSilence.Doors;
using HouseOfSilence.Interaction;
using HouseOfSilence.Items;
using HouseOfSilence.Lights;
using HouseOfSilence.Objectives;
using UnityEditor;
using UnityEngine;

namespace HouseOfSilence.EditorTools.Level
{
    /// <summary>
    /// Construit la maison principale a partir des modules de
    /// Assets/ModularHousePack1 : rez-de-chaussee, deux etages, toit,
    /// grand sous-sol avec tunnel et sortie de secours.
    ///
    /// Le pack travaille sur une grille de 2.5 m :
    ///  - sols 2.5 x 2.5 (epaisseur 0.2, dessus a +0.1), pivot au centre ;
    ///  - murs 2.5 x 2.5, pivot en bas au centre, poses sur les aretes des
    ///    dalles, face exterieure = +Z local ;
    ///  - modules porte / fenetre = un mur perce, meme convention ;
    ///  - Corner_1A = deux murs exterieurs en L, pose au centre d'une dalle
    ///    d'angle (0 = angle NO, 90 = NE, 180 = SE, 270 = SO) ;
    ///  - Stairs_1A = escalier tournant dans une seule dalle, monte de 2.5 m :
    ///    entree par le sud (moitie ouest), palier d'arrivee en haut au
    ///    sud-est, ouvert au sud et a l'est ; la dalle du niveau superieur
    ///    n'est pas posee (le module fournit le palier). Le haut de la
    ///    deuxieme volee debouche dans l'angle nord-est, a 25 cm du bord :
    ///    l'arete EST du niveau d'arrivee doit rester ouverte (sans mur ni
    ///    porte) sinon le joueur ne passe pas entre le poteau et le mur ;
    ///  - toits poses au sommet des murs : RoofA (pente, bord bas = +Z),
    ///    RoofB (angle), RoofFlat (interieur), RoofClosed (joint pente/plat).
    ///
    /// Les portes du pack sont demontees : le battant est reparente sous
    /// une charniere pilotee par nos scripts DoorBase, le reste du module
    /// (mur + dormant) reste statique.
    ///
    /// Repere local : x = 0..17.5 (ouest -> est, 7 dalles), z = 0..12.5
    /// (sud -> nord, 5 dalles), y = 0 au rez-de-chaussee. Facade au sud.
    /// </summary>
    public static class PrototypeHouseBuilder
    {
        public const float Tile = 2.5f;
        public const int TilesX = 7;
        public const int TilesZ = 5;

        public const float Width = TilesX * Tile;   // 17.5
        public const float Depth = TilesZ * Tile;   // 12.5
        public const float Level = 2.5f;
        public const float BasementY = -Level;

        /// <summary>Le tunnel file vers l'est sous le terrain (dalles j = 3) et remonte dans un abri.</summary>
        public const float TunnelMinZ = 3 * Tile;         // 7.5
        public const float TunnelMaxZ = 4 * Tile;         // 10
        public const int TunnelFirstTile = TilesX;        // 7
        public const int TunnelLastTile = 13;             // dalle de l'escalier de sortie
        /// <summary>L'abri couvre deux dalles : la cage d'escalier et son entree, sinon le mur ouest bloque la tete dans la montee.</summary>
        public const float ShedMinX = (TunnelLastTile - 1) * Tile;  // 30
        public const float ShedMaxX = (TunnelLastTile + 1) * Tile;  // 35
        public const float TunnelEndX = ShedMaxX;
        /// <summary>L'abri descend d'une dalle au sud de la cage (j = 2) : c'est la que debouche l'escalier et que s'ouvre la porte.</summary>
        public const int ShedExitTileJ = 2;
        public const float ShedMinZ = ShedExitTileJ * Tile;         // 5

        private const float FloorTop = 0.1f;
        private const float RoomHeight = 2.4f;

        private const string ModulesFolder = "Assets/ModularHousePack1/Prefabs/Modules";
        private const string MaterialsFolder = "Assets/_Game/Materials";
        private const string ItemsFolder = "Assets/_Game/ScriptableObjects/Items";
        private const string ObjectivesFolder = "Assets/_Game/ScriptableObjects/Objectives";

        private enum Side { N, E, S, W }

        private static readonly string[] ModuleSubfolders = { "Walls", "Doors", "Window", "Corners", "Floors", "Roofs", "Stairs", "Curtains" };
        private static readonly Dictionary<string, GameObject> s_Modules = new Dictionary<string, GameObject>();

        private static Transform s_Root;
        private static Material s_Wood, s_Stone;
        internal static Material WoodMaterial { get { return s_Wood; } }
        internal static Material StoneMaterial { get { return s_Stone; } }

        // ------------------------------------------------------------------

        /// <summary>Construit la maison. L'origine locale (0,0,0) est le coin sud-ouest, au niveau du rez.</summary>
        public static GameObject Build(Vector3 worldOrigin)
        {
            s_Modules.Clear();
            LoadMaterials();

            GameObject house = new GameObject("House");
            house.transform.position = worldOrigin;
            s_Root = house.transform;

            BuildBasement();
            BuildGroundFloor();
            BuildFirstFloor();
            BuildSecondFloor();
            BuildRoof();

            return house;
        }

        // ==================================================================
        // SOUS-SOL : y = -2.5 ; briques apparentes, cloisons platre
        // ==================================================================

        private static void BuildBasement()
        {
            Transform floor = Group(s_Root, "Floor_Basement");
            Transform floors = Group(floor, "Floors");
            Transform walls = Group(floor, "Walls");
            Transform rooms = Group(floor, "Rooms");
            Transform doors = Group(floor, "Doors");
            Transform lights = Group(floor, "Lights");
            Transform gameplay = Group(floor, "Gameplay");

            float y = BasementY;

            // Sol sombre (RoofFloor : dessus a y+0, donc pose 10 cm plus haut pour aligner a +0.1).
            for (int i = 0; i < TilesX; i++)
                for (int j = 0; j < TilesZ; j++)
                    Module(floors, "RoofFloor", TileCenter(i, j, y + FloorTop), 0f);

            // Perimetre en briques (mur exterieur retourne : la brique regarde l'interieur).
            // L'arete est de la dalle (6,3) reste ouverte : c'est le depart du tunnel.
            for (int i = 0; i < TilesX; i++)
            {
                Module(walls, "Wall_1A", EdgePos(i, 0, Side.S, y), Yaw(Side.S) + 180f);

                if (i != 2)
                {
                    // (2,4) : le module d'escalier apporte son propre mur nord en platre ;
                    // la brique posee par dessus passait devant et se voyait depuis la buanderie.
                    Module(walls, "Wall_1A", EdgePos(i, TilesZ - 1, Side.N, y), Yaw(Side.N) + 180f);
                }
            }

            for (int j = 0; j < TilesZ; j++)
            {
                Module(walls, "Wall_1A", EdgePos(0, j, Side.W, y), Yaw(Side.W) + 180f);

                if (j != 3)
                {
                    Module(walls, "Wall_1A", EdgePos(TilesX - 1, j, Side.E, y), Yaw(Side.E) + 180f);
                }
            }

            // --- Escalier sous-sol -> rez dans la dalle (2,4), tourne a 90 : on y entre
            // depuis la cave (1,4), on arrive au rez face a la buanderie (2,3). Le passage
            // sous le palier relie la cave au hall (2,3). Ses murs propres ferment le nord
            // (perimetre) et l'est (x = 7.5, vers le hall).
            Stairs(floor, 2, 4, y, 90f, "RoofFloor", "Floor1");

            // --- Cloisons ----------------------------------------------------------
            // Cave (ouest, i 0..1) | hall (i 2..3) : x = 5, porte en j = 2. j = 4 : pied de l'escalier, ouvert.
            Wall(walls, 1, 0, Side.E, y);
            Wall(walls, 1, 1, Side.E, y);
            Wall(walls, 1, 3, Side.E, y);
            // Stockage (i 2..3, j 0..1) sous le hall : z = 5, porte en i = 3.
            Wall(walls, 2, 1, Side.N, y);
            // Hall | aile est : x = 10, portes en j = 2 (atelier) et j = 3 (chaufferie).
            Wall(walls, 3, 0, Side.E, y);
            Wall(walls, 3, 1, Side.E, y);
            Wall(walls, 3, 4, Side.E, y);
            // Atelier (i 4..5, j 0..2) | chaufferie (i 4..5, j 3..4) : z = 7.5.
            Wall(walls, 4, 2, Side.N, y);
            Wall(walls, 5, 2, Side.N, y);
            // Piece secrete (i 6, j 0..2) : x = 15, porte en j = 1 ; reserve du tunnel (i 6, j 3..4) : porte en j = 3.
            Wall(walls, 5, 0, Side.E, y);
            Wall(walls, 5, 2, Side.E, y);
            Wall(walls, 5, 4, Side.E, y);
            Wall(walls, 6, 2, Side.N, y);

            // --- Portes ------------------------------------------------------------
            Door<Door>(walls, doors, "Door_Cellar", 1, 2, Side.E, y, "Porte de la cave", null);
            Door<Door>(walls, doors, "Door_Storage", 3, 1, Side.N, y, "Porte du stockage", null);
            Door<Door>(walls, doors, "Door_Workshop", 3, 2, Side.E, y, "Porte de l'atelier", null);
            Door<Door>(walls, doors, "Door_BoilerRoom", 3, 3, Side.E, y, "Porte de la chaufferie", null);
            Door<Door>(walls, doors, "Door_TunnelRoom", 5, 3, Side.E, y, "Porte de la reserve", null);
            Door<ObjectiveDoor>(walls, doors, "Door_SecretRoom", 5, 1, Side.E, y, "Porte etrange", door =>
            {
                SetString(door, "requiredObjectiveId", "find_symbol");
                SetString(door, "blockedMessage", "Un symbole est grave dans le bois. Rien ne bouge.");
            });

            // --- Tunnel : dalles i 7..13, j 3 --------------------------------------
            Transform tunnel = Group(floor, "Tunnel");

            for (int i = TunnelFirstTile; i <= TunnelLastTile; i++)
            {
                Module(tunnel, "RoofFloor", TileCenter(i, 3, y + FloorTop), 0f);
                Module(tunnel, "Wall_1A", EdgePos(i, 3, Side.S, y), Yaw(Side.S) + 180f);

                if (i != TunnelLastTile)
                {
                    // Le module d'escalier apporte son propre mur au nord de la derniere dalle.
                    Module(tunnel, "Wall_1A", EdgePos(i, 3, Side.N, y), Yaw(Side.N) + 180f);
                }

                if (i < TunnelLastTile - 1)
                {
                    // Plafond juste sous la surface du terrain (terrain a +0.05 local).
                    Module(tunnel, "Floor1", TileCenter(i, 3, -0.15f), 0f);
                }
            }

            // Escalier de sortie : entree depuis l'ouest (tunnel), monte vers l'abri,
            // debouche en haut contre l'arete sud de la dalle 13.
            Stairs(tunnel, TunnelLastTile, 3, y, 90f, "RoofFloor", "Floor1");

            // Abri de sortie au niveau du sol, en L sur trois dalles : (12,3) est a la
            // fois plafond du tunnel et sol de l'abri (un mur ouest y bloquerait la tete
            // dans la montee), (13,3) est la cage, (13,2) recoit l'arrivee et la porte.
            Transform shed = Group(floor, "ExitShed");
            int shedFirst = TunnelLastTile - 1;
            int exitJ = ShedExitTileJ;
            Module(shed, "Floor1", TileCenter(shedFirst, 3, 0f), 0f);
            Module(shed, "Floor1", TileCenter(TunnelLastTile, exitJ, 0f), 0f);
            Module(shed, "Wall_1A", EdgePos(shedFirst, 3, Side.W, 0f), Yaw(Side.W));
            Module(shed, "Wall_1A", EdgePos(shedFirst, 3, Side.N, 0f), Yaw(Side.N));
            Module(shed, "Wall_1A", EdgePos(shedFirst, 3, Side.S, 0f), Yaw(Side.S));
            Module(shed, "Wall_1A", EdgePos(TunnelLastTile, 3, Side.N, 0f), Yaw(Side.N));
            Module(shed, "Wall_1A", EdgePos(TunnelLastTile, 3, Side.E, 0f), Yaw(Side.E));
            Module(shed, "Wall_1A", EdgePos(TunnelLastTile, exitJ, Side.W, 0f), Yaw(Side.W));
            Module(shed, "Wall_1A", EdgePos(TunnelLastTile, exitJ, Side.E, 0f), Yaw(Side.E));
            Module(shed, "RoofFloor", TileCenter(shedFirst, 3, Level), 0f);
            Module(shed, "RoofFloor", TileCenter(TunnelLastTile, 3, Level), 0f);
            Module(shed, "RoofFloor", TileCenter(TunnelLastTile, exitJ, Level), 0f);
            ExteriorDoor<Door>(shed, doors, "Door_TunnelExit", TunnelLastTile, exitJ, Side.S, 0f, "Sortie de secours", null);

            // --- Pieces ------------------------------------------------------------
            Room(rooms, "Cave", 0, 1, 0, 4, y, -1);
            Room(rooms, "Hall du sous-sol", 2, 3, 2, 4, y, -1);
            Room(rooms, "Stockage", 2, 3, 0, 1, y, -1);
            Room(rooms, "Atelier", 4, 5, 0, 2, y, -1);
            Room(rooms, "Chaufferie", 4, 5, 3, 4, y, -1);
            Room(rooms, "Piece secrete", 6, 6, 0, 2, y, -1);
            Room(rooms, "Reserve", 6, 6, 3, 4, y, -1);
            Room(rooms, "Tunnel", TunnelFirstTile, TunnelLastTile, 3, 3, y, -1);

            // --- Lumieres ----------------------------------------------------------
            Bulb(lights, "Bulb_BasementHall", RoomPoint(2, 3, 3, 3, y, 2.2f), new Color(1f, 0.82f, 0.55f), 1.1f, 6f, true, false, 2f);
            Bulb(lights, "Bulb_Cellar", RoomPoint(0, 1, 2, 2, y, 2.2f), new Color(1f, 0.8f, 0.5f), 0.8f, 6f, false, true, 1f);
            Bulb(lights, "Bulb_Storage", RoomPoint(2, 3, 0, 1, y, 2.2f), new Color(1f, 0.88f, 0.7f), 1.2f, 6f, true, true, 0.4f);
            Bulb(lights, "Bulb_BoilerRoom", RoomPoint(4, 5, 3, 4, y, 2.2f), new Color(1f, 0.88f, 0.7f), 1.4f, 7f, true, true, 0.4f);
            Bulb(lights, "Bulb_Workshop", RoomPoint(4, 5, 0, 2, y, 2.2f), new Color(0.95f, 0.85f, 0.7f), 1f, 7f, false, true, 0.4f);
            Bulb(lights, "Bulb_Tunnel", new Vector3(26.25f, y + 2.2f, 8.75f), new Color(1f, 0.8f, 0.5f), 0.8f, 5f, true, false, 4f);
            Bulb(lights, "Bulb_TunnelEnd", new Vector3(31.25f, y + 2.2f, 8.75f), new Color(1f, 0.8f, 0.5f), 0.7f, 5f, true, false, 3f);

            // --- Gameplay ----------------------------------------------------------
            // Tableau electrique + disjoncteur dans la chaufferie, contre son mur ouest (x = 10).
            GameObject fuseBox = LightSetupMenu.BuildSwitch("FuseBox", Vector3.zero, new Vector3(0.12f, 0.7f, 0.5f));
            Attach(fuseBox, gameplay, new Vector3(10.12f, y + 1.5f, 11.2f));
            FuseBox fuse = fuseBox.AddComponent<FuseBox>();
            EditorSetupUtility.SetObjectField(fuse, "requiredItem", LoadItem("Item_Fuse"));
            AttachObjective(fuseBox, "Objective_03_RepairFusebox");

            GameObject breaker = LightSetupMenu.BuildSwitch("PowerSwitch", Vector3.zero, new Vector3(0.12f, 0.5f, 0.3f));
            Attach(breaker, gameplay, new Vector3(10.12f, y + 1.5f, 12.0f));
            breaker.AddComponent<PowerSwitch>();
            AttachObjective(breaker, "Objective_04_RestorePower");

            Switch(gameplay, "Switch_BasementHall", new Vector3(9.9f, y + 1.3f, 6.2f), 6f);
            Switch(gameplay, "Switch_BoilerRoom", new Vector3(10.1f, y + 1.3f, 9.2f), 5f);
            Switch(gameplay, "Switch_Storage", new Vector3(7.4f, y + 1.3f, 4.4f), 5f);
            Switch(gameplay, "Switch_Workshop", new Vector3(10.1f, y + 1.3f, 5.5f), 6f);

            Zone(gameplay, "Zone_ExploreBasement", RoomPoint(4, 5, 3, 4, y, 1.4f), new Vector3(4.5f, 2.6f, 4.5f), "Objective_05_ExploreBasement");
            Zone(gameplay, "Zone_FindSymbol", new Vector3(13.75f, y + 1.4f, 1.6f), new Vector3(3f, 2.6f, 2.6f), "Objective_06_FindSymbol");

            // Decor minimal : chaudiere, etageres, caisses, autel.
            Box(gameplay, "Boiler", 12.6f, 14.4f, y + FloorTop, y + 2.0f, 11.4f, 12.3f, s_Stone);
            Box(gameplay, "Shelf_Storage", 9.4f, 9.9f, y + FloorTop, y + 2.1f, 0.4f, 4.4f, s_Wood);
            Box(gameplay, "Crate_A", 5.4f, 6.3f, y + FloorTop, y + 1.0f, 0.5f, 1.4f, s_Wood);
            Box(gameplay, "Crate_B", 5.6f, 6.2f, y + 1.0f, y + 1.6f, 0.6f, 1.2f, s_Wood);
            Box(gameplay, "Workbench", 10.4f, 13.4f, y + FloorTop, y + 1.0f, 0.4f, 1.1f, s_Wood);
            Box(gameplay, "WineRack", 0.3f, 0.9f, y + FloorTop, y + 2.2f, 1f, 11.5f, s_Wood);
            Box(gameplay, "Altar", 15.6f, 16.9f, y + FloorTop, y + 0.9f, 1.3f, 3.1f, s_Stone);

            Pickup(gameplay, "Item_CursedObject", new Vector3(16.25f, y + 1.0f, 2.2f), 1);
            Pickup(gameplay, "Item_Battery", new Vector3(5.9f, y + 1.7f, 0.9f), 2);
        }

        // ==================================================================
        // REZ-DE-CHAUSSEE : y = 0
        // ==================================================================

        private static void BuildGroundFloor()
        {
            Transform floor = Group(s_Root, "Floor_Ground");
            Transform floors = Group(floor, "Floors");
            Transform walls = Group(floor, "Walls");
            Transform rooms = Group(floor, "Rooms");
            Transform doors = Group(floor, "Doors");
            Transform lights = Group(floor, "Lights");
            Transform gameplay = Group(floor, "Gameplay");

            float y = 0f;

            // Dalles carrelees ; la dalle (2,4) est le palier de l'escalier du sous-sol.
            FloorTiles(floors, "Floor1", y, (i, j) => i == 2 && j == 4);

            // Enveloppe : angles, murs, fenetres. La porte d'entree (3,0 sud) et la
            // porte de la cuisine (5,4 nord) sont posees a part.
            Envelope(walls, y, (i, j, side) =>
            {
                if (side == Side.S && i == 3) return null;
                if (side == Side.N && i == 5) return null;
                if (side == Side.S && (i == 1 || i == 5)) return "Window_1B";
                if (side == Side.N && (i == 2 || i == 4)) return "Window_1B";
                if (side == Side.W && (j == 1 || j == 3)) return "Window_1B";
                if (side == Side.E && (j == 1 || j == 3)) return "Window_1B";
                return "Wall_1B";
            });

            ExteriorDoor<Door>(walls, doors, "Door_Front", 3, 0, Side.S, y, "Porte d'entree", null);
            ExteriorDoor<Door>(walls, doors, "Door_Back", 5, 4, Side.N, y, "Porte de la cuisine", door =>
            {
                SetBool(door, "startLocked", true);
                SetString(door, "lockedPromptText", "Verrouillee de l'exterieur");
            });

            // --- Escalier rez -> 1er au bout nord du couloir (dalle 3,4) ------------
            Stairs(floor, 3, 4, y, 0f, "Floor1", "Floor2");

            // --- Cloisons ----------------------------------------------------------
            // Cage de l'escalier du sous-sol (2,4), tournee a 90 : son garde-corps
            // borde la tremie cote ouest, le palier s'ouvre sur la buanderie (1,4) et
            // l'arrivee debouche vers (2,3). Le mur est est celui du module de
            // l'escalier du rez (3,4) ; rien a ajouter.
            // Couloir i = 3. Cote ouest x = 7.5 : bureau (j 0..1) porte en j = 1,
            // buanderie (j 2..4) porte a cle en j = 2, j = 4 = mur du module escalier.
            Wall(walls, 2, 0, Side.E, y);
            Wall(walls, 2, 3, Side.E, y);
            // Cote est x = 10 : salon (j 0..1) porte en j = 1, cuisine (j 2..4) porte en j = 3,
            // j = 4 : ouvert sous l'escalier (passage vers la cuisine).
            Wall(walls, 3, 0, Side.E, y);
            Wall(walls, 3, 2, Side.E, y);
            // Bureau | buanderie : z = 5.
            for (int i = 0; i <= 2; i++) Wall(walls, i, 1, Side.N, y);
            // Salon | cuisine : z = 5.
            for (int i = 4; i <= 6; i++) Wall(walls, i, 1, Side.N, y);
            // Cuisine | cellier : x = 15, porte en j = 3.
            Wall(walls, 5, 2, Side.E, y);
            Wall(walls, 5, 4, Side.E, y);

            // --- Portes ------------------------------------------------------------
            Door<Door>(walls, doors, "Door_Office", 2, 1, Side.E, y, "Porte du bureau", null);
            Door<KeyDoor>(walls, doors, "Door_Basement", 2, 2, Side.E, y, "Porte de la buanderie", door =>
            {
                SetString(door, "lockId", "basement");
                SetString(door, "missingKeyPrompt", "Verrouillee - il faut la cle du sous-sol");
            });
            Door<Door>(walls, doors, "Door_Living", 3, 1, Side.E, y, "Porte du salon", null);
            Door<Door>(walls, doors, "Door_Kitchen", 3, 3, Side.E, y, "Porte de la cuisine", null);
            Door<Door>(walls, doors, "Door_Pantry", 5, 3, Side.E, y, "Porte du cellier", null);

            // --- Pieces ------------------------------------------------------------
            Room(rooms, "Bureau", 0, 2, 0, 1, y, 0);
            Room(rooms, "Buanderie", 0, 2, 2, 4, y, 0);
            Room(rooms, "Couloir", 3, 3, 0, 4, y, 0);
            Room(rooms, "Salon", 4, 6, 0, 1, y, 0);
            Room(rooms, "Cuisine", 4, 5, 2, 4, y, 0);
            Room(rooms, "Cellier", 6, 6, 2, 4, y, 0);

            // --- Lumieres ----------------------------------------------------------
            Bulb(lights, "Bulb_Hall", RoomPoint(3, 3, 1, 1, y, 2.2f), new Color(1f, 0.85f, 0.6f), 1.2f, 6f, true, false, 0.6f);
            Bulb(lights, "Bulb_Corridor", RoomPoint(3, 3, 3, 3, y, 2.2f), new Color(1f, 0.85f, 0.6f), 0.9f, 6f, true, false, 1.5f);
            Bulb(lights, "Bulb_Office", RoomPoint(0, 2, 0, 1, y, 2.2f), new Color(1f, 0.9f, 0.7f), 1.2f, 7f, true, true, 0.3f);
            Bulb(lights, "Bulb_Laundry", RoomPoint(0, 1, 2, 4, y, 2.2f), new Color(1f, 0.85f, 0.6f), 0.8f, 6f, false, false, 2f);
            Bulb(lights, "Bulb_Living", RoomPoint(4, 6, 0, 1, y, 2.2f), new Color(1f, 0.9f, 0.7f), 1.5f, 8f, true, true, 0.3f);
            Bulb(lights, "Bulb_Kitchen", RoomPoint(4, 5, 2, 4, y, 2.2f), new Color(0.95f, 0.9f, 0.75f), 1.3f, 7f, true, false, 1.2f);
            Bulb(lights, "Bulb_Pantry", RoomPoint(6, 6, 2, 4, y, 2.2f), new Color(1f, 0.9f, 0.7f), 0.9f, 5f, true, true, 0.3f);

            // Lampe du porche, dehors au dessus de la porte d'entree.
            Bulb(lights, "Bulb_Porch", new Vector3(8.75f, 2.35f, -0.6f), new Color(1f, 0.75f, 0.45f), 1f, 7f, true, false, 2.5f);

            // --- Interrupteurs -------------------------------------------------------
            Switch(gameplay, "Switch_Hall", new Vector3(7.6f, 1.3f, 1.2f), 4f);
            Switch(gameplay, "Switch_Office", new Vector3(7.4f, 1.3f, 2.6f), 6f);
            Switch(gameplay, "Switch_Living", new Vector3(10.1f, 1.3f, 2.6f), 7f);
            Switch(gameplay, "Switch_Kitchen", new Vector3(10.1f, 1.3f, 7.6f), 6f);
            Switch(gameplay, "Switch_Laundry", new Vector3(7.4f, 1.3f, 7.2f), 6f);

            // --- Objets et objectifs -------------------------------------------------
            Box(gameplay, "HallTable", 7.7f, 8.4f, y + FloorTop, y + 0.85f, 2.3f, 3.2f, s_Wood);
            Pickup(gameplay, "Item_Flashlight", new Vector3(8.05f, y + 0.95f, 2.75f), 1);

            Box(gameplay, "Desk", 0.5f, 2.3f, y + FloorTop, y + 0.8f, 3.6f, 4.4f, s_Wood);
            Pickup(gameplay, "Item_Key_Basement", new Vector3(1.4f, y + 0.9f, 4f), 1);

            Box(gameplay, "KitchenCounter", 10.3f, 14.7f, y + FloorTop, y + 0.95f, 11.6f, 12.3f, s_Wood);
            Pickup(gameplay, "Item_Fuse", new Vector3(11.5f, y + 1.05f, 11.95f), 1);

            Box(gameplay, "Sofa", 11f, 13.4f, y + FloorTop, y + 0.75f, 0.6f, 1.5f, s_Wood);
            Box(gameplay, "Bookshelf", 16.9f, 17.4f, y + FloorTop, y + 2.2f, 0.6f, 3.6f, s_Wood);
            Box(gameplay, "PantryShelf", 16.9f, 17.4f, y + FloorTop, y + 2.1f, 6f, 11f, s_Wood);
            Box(gameplay, "WashingMachine", 0.4f, 1.1f, y + FloorTop, y + 0.95f, 9.5f, 10.2f, s_Stone);

            // Sortie : revenir devant la porte d'entree, dehors.
            Zone(gameplay, "Zone_ReturnToExit", new Vector3(8.75f, 1.5f, -2.5f), new Vector3(4f, 3f, 3f), "Objective_08_ReturnToExit");
        }

        // ==================================================================
        // PREMIER ETAGE : y = 2.5
        // ==================================================================

        private static void BuildFirstFloor()
        {
            Transform floor = Group(s_Root, "Floor_First");
            Transform floors = Group(floor, "Floors");
            Transform walls = Group(floor, "Walls");
            Transform rooms = Group(floor, "Rooms");
            Transform doors = Group(floor, "Doors");
            Transform lights = Group(floor, "Lights");
            Transform gameplay = Group(floor, "Gameplay");

            float y = Level;

            // Parquet ; (3,4) = palier d'arrivee de l'escalier du rez.
            FloorTiles(floors, "Floor2", y, (i, j) => i == 3 && j == 4);

            Envelope(walls, y, (i, j, side) =>
            {
                if (side == Side.S && (i == 1 || i == 5)) return "Window_1B";
                if (side == Side.N && (i == 1 || i == 4)) return "Window_1B";
                if (side == Side.W && (j == 1 || j == 3)) return "Window_1B";
                if (side == Side.E && (j == 1 || j == 3)) return "Window_1B";
                return "Wall_1B";
            });

            // --- Escalier 1er -> 2e au bout sud du couloir (dalle 3,0), retourne : entree par le nord.
            Stairs(floor, 3, 0, y, 180f, "Floor2", "Floor2");

            // --- Cloisons ----------------------------------------------------------
            // Autour du palier (3,4) : garde-corps du module, mur a l'ouest ; l'est reste
            // ouvert sur (4,4) qui devient le palier d'arrivee (le haut de la volee debouche
            // contre cette arete). La porte du debarras est repoussee en (4,4) est.
            Wall(walls, 2, 4, Side.E, y);

            // Couloir cote ouest x = 7.5 : placard sous l'escalier (2,0) ouvert par l'alcove du module,
            // chambre d'amis (j 1..2) porte en j = 1, salle de bain (j 3..4) porte en j = 3.
            Wall(walls, 2, 2, Side.E, y);
            // Placard (2,0) : ferme a l'ouest et au nord.
            Wall(walls, 2, 0, Side.W, y);
            Wall(walls, 2, 0, Side.N, y);
            // Salle d'eau (i 0..1, j 0) : porte depuis la chambre d'amis en i = 1.
            Wall(walls, 0, 0, Side.N, y);
            // Chambre d'amis | salle de bain : z = 7.5.
            for (int i = 0; i <= 2; i++) Wall(walls, i, 2, Side.N, y);

            // Couloir cote est x = 10 : j = 0 mur du module escalier, chambre principale (j 0..1) porte en j = 1,
            // chambre d'enfant (i 4..5, j 2..3) porte en j = 2, j = 3 mur.
            Wall(walls, 3, 3, Side.E, y);
            // Chambre principale | chambre d'enfant : z = 5.
            for (int i = 4; i <= 6; i++) Wall(walls, i, 1, Side.N, y);
            // Chambre d'enfant | palier + debarras (i 4..6, j 4) : z = 10.
            for (int i = 4; i <= 6; i++) Wall(walls, i, 3, Side.N, y);
            // Chambre d'enfant | salle de jeux (i 6, j 2..3) : x = 15, porte en j = 2.
            Wall(walls, 5, 3, Side.E, y);

            // --- Portes ------------------------------------------------------------
            Door<Door>(walls, doors, "Door_GuestRoom", 2, 1, Side.E, y, "Chambre d'amis", null);
            Door<Door>(walls, doors, "Door_Bathroom", 2, 3, Side.E, y, "Salle de bain", null);
            Door<Door>(walls, doors, "Door_WaterCloset", 1, 0, Side.N, y, "Salle d'eau", null);
            Door<Door>(walls, doors, "Door_MasterBedroom", 3, 1, Side.E, y, "Chambre principale", null);
            Door<Door>(walls, doors, "Door_ChildRoom", 3, 2, Side.E, y, "Chambre d'enfant", null);
            Door<Door>(walls, doors, "Door_PlayRoom", 5, 2, Side.E, y, "Salle de jeux", null);
            Door<Door>(walls, doors, "Door_UpstairsStorage", 4, 4, Side.E, y, "Porte du debarras", null);

            // --- Pieces ------------------------------------------------------------
            Room(rooms, "Salle d'eau", 0, 1, 0, 0, y, 1);
            Room(rooms, "Placard", 2, 2, 0, 0, y, 1);
            Room(rooms, "Chambre d'amis", 0, 2, 1, 2, y, 1);
            Room(rooms, "Salle de bain", 0, 2, 3, 4, y, 1);
            Room(rooms, "Couloir 1er", 3, 3, 0, 4, y, 1);
            Room(rooms, "Couloir 1er", 4, 4, 4, 4, y, 1); // palier d'arrivee de l'escalier
            Room(rooms, "Chambre principale", 4, 6, 0, 1, y, 1);
            Room(rooms, "Chambre d'enfant", 4, 5, 2, 3, y, 1);
            Room(rooms, "Salle de jeux", 6, 6, 2, 3, y, 1);
            Room(rooms, "Debarras", 5, 6, 4, 4, y, 1);

            // --- Lumieres ----------------------------------------------------------
            Bulb(lights, "Bulb_Corridor1", RoomPoint(3, 3, 2, 2, y, 2.2f), new Color(1f, 0.85f, 0.6f), 0.8f, 6f, true, false, 2.5f);
            Bulb(lights, "Bulb_MasterBedroom", RoomPoint(4, 6, 0, 1, y, 2.2f), new Color(1f, 0.8f, 0.55f), 0.7f, 7f, true, false, 0.8f);
            Bulb(lights, "Bulb_ChildRoom", RoomPoint(4, 5, 2, 3, y, 2.2f), new Color(0.9f, 0.85f, 1f), 0.6f, 5f, true, true, 0.5f);
            Bulb(lights, "Bulb_Bathroom", RoomPoint(0, 2, 3, 4, y, 2.2f), new Color(0.85f, 0.95f, 1f), 1f, 6f, true, true, 1f);
            Bulb(lights, "Bulb_Guest", RoomPoint(0, 2, 1, 2, y, 2.2f), new Color(1f, 0.9f, 0.7f), 0.9f, 6f, false, true, 0.3f);
            Bulb(lights, "Bulb_Storage1", RoomPoint(5, 6, 4, 4, y, 2.2f), new Color(1f, 0.85f, 0.6f), 0.6f, 5f, false, true, 1.5f);

            Switch(gameplay, "Switch_Corridor1", new Vector3(9.9f, y + 1.3f, 8.6f), 4f);
            Switch(gameplay, "Switch_Master", new Vector3(10.1f, y + 1.3f, 2.6f), 7f);
            Switch(gameplay, "Switch_Child", new Vector3(10.1f, y + 1.3f, 6.4f), 5f);
            Switch(gameplay, "Switch_Guest", new Vector3(7.4f, y + 1.3f, 2.6f), 6f);

            Box(gameplay, "Bed_Master", 13f, 15f, y + FloorTop, y + 0.6f, 0.5f, 2.6f, s_Wood);
            Box(gameplay, "Bed_Child", 10.6f, 11.6f, y + FloorTop, y + 0.55f, 8.6f, 9.6f, s_Wood);
            Box(gameplay, "Bed_Guest", 0.5f, 1.6f, y + FloorTop, y + 0.55f, 4.5f, 6.6f, s_Wood);
            Box(gameplay, "Wardrobe", 15.4f, 17.2f, y + FloorTop, y + 2.1f, 4.3f, 4.9f, s_Wood);
            Box(gameplay, "Bathtub", 0.4f, 1.2f, y + FloorTop, y + 0.65f, 8f, 9.8f, s_Stone);
            Box(gameplay, "StorageShelf", 12.8f, 16.5f, y + FloorTop, y + 0.6f, 11.8f, 12.3f, s_Wood);

            Pickup(gameplay, "Item_Key_Office", new Vector3(14.5f, y + 0.7f, 12.05f), 1);
            Pickup(gameplay, "Item_Battery", new Vector3(5.5f, y + 0.4f, 11.5f), 1);
        }

        // ==================================================================
        // DEUXIEME ETAGE (combles amenages) : y = 5
        // ==================================================================

        private static void BuildSecondFloor()
        {
            Transform floor = Group(s_Root, "Floor_Second");
            Transform floors = Group(floor, "Floors");
            Transform walls = Group(floor, "Walls");
            Transform rooms = Group(floor, "Rooms");
            Transform doors = Group(floor, "Doors");
            Transform lights = Group(floor, "Lights");
            Transform gameplay = Group(floor, "Gameplay");

            float y = 2f * Level;

            // (3,0) = palier d'arrivee de l'escalier du 1er.
            FloorTiles(floors, "Floor2", y, (i, j) => i == 3 && j == 0);

            Envelope(walls, y, (i, j, side) =>
            {
                if (side == Side.S && (i == 1 || i == 5)) return "Window_1B";
                if (side == Side.N && i == 4) return "Window_1B";
                if (side == Side.W && j == 2) return "Window_1B";
                if (side == Side.E && j == 2) return "Window_1B";
                return "Wall_1B";
            });

            // --- Cloisons ----------------------------------------------------------
            // Autour du palier (3,0) : le module (tourne a 180) n'a pas de mur au dessus
            // de 2.5 m ; on ferme l'est, et l'ouest reste ouvert sur (2,0) qui devient
            // le palier d'arrivee (le haut de la volee debouche contre cette arete).
            Wall(walls, 3, 0, Side.E, y);
            // Couloir cote ouest x = 7.5 : chambre de bonne (j 1..2) porte en j = 1, grenier ouest (j 3..4) porte en j = 3.
            Wall(walls, 2, 2, Side.E, y);
            Wall(walls, 2, 4, Side.E, y);
            // Chambre de bonne | grenier ouest : z = 7.5.
            for (int i = 0; i <= 2; i++) Wall(walls, i, 2, Side.N, y);
            // Chambre de bonne | reduit (i 0..1, j 0) et palier (2,0) : z = 2.5, porte en i = 1.
            Wall(walls, 0, 0, Side.N, y);
            Wall(walls, 2, 0, Side.N, y);
            // Reduit | palier : x = 5.
            Wall(walls, 1, 0, Side.E, y);
            // Couloir cote est x = 10 : grenier (i 4..6, j 1..4) porte en j = 2.
            Wall(walls, 3, 1, Side.E, y);
            Wall(walls, 3, 3, Side.E, y);
            Wall(walls, 3, 4, Side.E, y);
            // Reserve du grenier (i 4..6, j 0) : z = 2.5, porte en i = 5.
            Wall(walls, 4, 0, Side.N, y);
            Wall(walls, 6, 0, Side.N, y);
            // Demi-cloison dans le grenier : x = 15, j 2..3 (passage en j 1 et 4).
            Wall(walls, 5, 2, Side.E, y);
            Wall(walls, 5, 3, Side.E, y);

            // --- Portes ------------------------------------------------------------
            Door<Door>(walls, doors, "Door_MaidRoom", 2, 1, Side.E, y, "Chambre de bonne", null);
            Door<Door>(walls, doors, "Door_AtticWest", 2, 3, Side.E, y, "Grenier ouest", null);
            Door<Door>(walls, doors, "Door_MaidCloset", 1, 0, Side.N, y, "Reduit", null);
            Door<Door>(walls, doors, "Door_Attic", 3, 2, Side.E, y, "Grenier", null);
            Door<Door>(walls, doors, "Door_AtticStore", 5, 0, Side.N, y, "Reserve", null);

            // --- Pieces ------------------------------------------------------------
            Room(rooms, "Reduit", 0, 1, 0, 0, y, 2);
            Room(rooms, "Chambre de bonne", 0, 2, 1, 2, y, 2);
            Room(rooms, "Grenier ouest", 0, 2, 3, 4, y, 2);
            Room(rooms, "Couloir 2e", 3, 3, 0, 4, y, 2);
            Room(rooms, "Couloir 2e", 2, 2, 0, 0, y, 2); // palier d'arrivee de l'escalier
            Room(rooms, "Reserve du grenier", 4, 6, 0, 0, y, 2);
            Room(rooms, "Grenier", 4, 6, 1, 4, y, 2);

            // Le deuxieme etage est presque sans lumiere : deux ampoules capricieuses.
            Bulb(lights, "Bulb_Corridor2", RoomPoint(3, 3, 2, 2, y, 2.2f), new Color(1f, 0.75f, 0.5f), 0.6f, 5f, true, false, 4f);
            Bulb(lights, "Bulb_Attic", RoomPoint(4, 5, 2, 3, y, 2.2f), new Color(1f, 0.85f, 0.6f), 0.9f, 8f, false, true, 0.3f);
            Bulb(lights, "Bulb_MaidRoom", RoomPoint(0, 2, 1, 2, y, 2.2f), new Color(1f, 0.85f, 0.6f), 0.7f, 6f, false, true, 1f);

            Switch(gameplay, "Switch_Attic", new Vector3(10.1f, y + 1.3f, 5.1f), 9f);
            Switch(gameplay, "Switch_Corridor2", new Vector3(9.9f, y + 1.3f, 8.6f), 4f);

            Pickup(gameplay, "Item_Crowbar", new Vector3(1.6f, y + 0.4f, 10.5f), 1);

            Box(gameplay, "Attic_Boxes_A", 15.5f, 17.2f, y + FloorTop, y + 1.3f, 9.5f, 12.2f, s_Wood);
            Box(gameplay, "Attic_Boxes_B", 10.5f, 12.5f, y + FloorTop, y + 0.9f, 10.5f, 12.2f, s_Wood);
            Box(gameplay, "OldWardrobe", 0.4f, 1.2f, y + FloorTop, y + 2.2f, 8f, 10f, s_Wood);
            Box(gameplay, "MaidBed", 0.5f, 1.5f, y + FloorTop, y + 0.55f, 3f, 5f, s_Wood);
        }

        // ==================================================================
        // TOIT : y = 7.5 (sommet des murs du 2e)
        // ==================================================================

        private static void BuildRoof()
        {
            Transform roof = Group(s_Root, "Roof");
            float y = 3f * Level;

            for (int i = 0; i < TilesX; i++)
            {
                for (int j = 0; j < TilesZ; j++)
                {
                    Vector3 c = TileCenter(i, j, y);

                    // Plafond du 2e etage.
                    Module(roof, "RoofFloor", c, 0f);

                    bool west = i == 0, east = i == TilesX - 1, south = j == 0, north = j == TilesZ - 1;

                    if (north && west) Module(roof, "RoofB", c, 0f);
                    else if (north && east) Module(roof, "RoofB", c, 90f);
                    else if (south && east) Module(roof, "RoofB", c, 180f);
                    else if (south && west) Module(roof, "RoofB", c, 270f);
                    else if (north) Module(roof, "RoofA", c, 0f);
                    else if (east) Module(roof, "RoofA", c, 90f);
                    else if (south) Module(roof, "RoofA", c, 180f);
                    else if (west) Module(roof, "RoofA", c, 270f);
                    else
                    {
                        Module(roof, "RoofFlat", c, 0f);

                        // Joints entre la partie plate et la couronne en pente.
                        if (j == TilesZ - 2) Module(roof, "RoofClosed", EdgePos(i, j, Side.N, y), 0f);
                        if (j == 1) Module(roof, "RoofClosed", EdgePos(i, j, Side.S, y), 0f);
                        if (i == 1) Module(roof, "RoofClosed", EdgePos(i, j, Side.W, y), 90f);
                        if (i == TilesX - 2) Module(roof, "RoofClosed", EdgePos(i, j, Side.E, y), 90f);
                    }
                }
            }

            // Cheminee en pierre sur le pan sud.
            Box(roof, "Chimney", 14f, 15f, y + 0.5f, y + 3f, 0.8f, 1.8f, s_Stone);
        }

        // ==================================================================
        // Grille
        // ==================================================================

        private static Vector3 TileCenter(int i, int j, float y)
        {
            return new Vector3(i * Tile + Tile * 0.5f, y, j * Tile + Tile * 0.5f);
        }

        private static Vector3 EdgePos(int i, int j, Side side, float y)
        {
            Vector3 c = TileCenter(i, j, y);
            float h = Tile * 0.5f;

            switch (side)
            {
                case Side.N: c.z += h; break;
                case Side.E: c.x += h; break;
                case Side.S: c.z -= h; break;
                default: c.x -= h; break;
            }

            return c;
        }

        /// <summary>Rotation qui oriente le +Z local d'un module vers l'exterieur de la dalle.</summary>
        private static float Yaw(Side side)
        {
            switch (side)
            {
                case Side.N: return 0f;
                case Side.E: return 90f;
                case Side.S: return 180f;
                default: return 270f;
            }
        }

        /// <summary>Point dans une piece definie par ses dalles (i0..i1, j0..j1), a la hauteur h au dessus du niveau.</summary>
        private static Vector3 RoomPoint(int i0, int i1, int j0, int j1, float y, float h)
        {
            return new Vector3((i0 + i1 + 1) * Tile * 0.5f, y + h, (j0 + j1 + 1) * Tile * 0.5f);
        }

        // ==================================================================
        // Modules
        // ==================================================================

        private static GameObject LoadModule(string moduleName)
        {
            GameObject prefab;

            if (s_Modules.TryGetValue(moduleName, out prefab))
            {
                return prefab;
            }

            foreach (string sub in ModuleSubfolders)
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModulesFolder + "/" + sub + "/" + moduleName + ".prefab");

                if (prefab != null)
                {
                    break;
                }
            }

            if (prefab == null)
            {
                Debug.LogError("[Level] Module introuvable dans ModularHousePack1 : " + moduleName);
            }

            s_Modules[moduleName] = prefab;
            return prefab;
        }

        /// <summary>Instancie un module du pack (lien prefab conserve), en coordonnees locales de la maison.</summary>
        private static GameObject Module(Transform parent, string moduleName, Vector3 localPosition, float yaw)
        {
            GameObject prefab = LoadModule(moduleName);

            if (prefab == null)
            {
                return null;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;

            if (instance == null)
            {
                return null;
            }

            instance.name = moduleName;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // Les murs exterieurs simples du pack (Wall_1A/1B/1C) n'ont pas de collider.
            if (moduleName.StartsWith("Wall_1") && instance.GetComponentInChildren<Collider>(true) == null)
            {
                BoxCollider box = instance.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, Tile * 0.5f, 0f);
                box.size = new Vector3(Tile, Tile, 0.2f);
            }

            MarkStatic(instance);
            return instance;
        }

        private static void MarkStatic(GameObject go)
        {
            const StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI;

            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            }
        }

        /// <summary>Cloison interieure sur une arete de dalle.</summary>
        private static void Wall(Transform parent, int i, int j, Side side, float y)
        {
            Module(parent, "Wall_2B", EdgePos(i, j, side, y), Yaw(side));
        }

        /// <summary>
        /// Cage d'escalier Stairs_1A posee dans la dalle (i, j) du niveau bas. Le module
        /// est livre tout en carrelage : on habille les marches avec le revetement du
        /// niveau de depart et le palier d'arrivee avec celui du niveau d'arrivee, pour
        /// ne pas retrouver un carre de carrelage au milieu du parquet.
        /// </summary>
        private static GameObject Stairs(Transform parent, int i, int j, float y, float yaw, string lowerFloorModule, string upperFloorModule)
        {
            GameObject stairs = Module(parent, "Stairs_1A", TileCenter(i, j, y), yaw);

            if (stairs == null)
            {
                return null;
            }

            ApplyFloorCovering(stairs, "Stairs1_Floor", lowerFloorModule);
            ApplyFloorCovering(stairs, "Stairs1_FloorTop", upperFloorModule);
            return stairs;
        }

        /// <summary>Remplace le materiau d'une piece du module escalier par le revetement d'un module de sol.</summary>
        private static void ApplyFloorCovering(GameObject stairs, string partName, string floorModule)
        {
            Material covering = FloorCoveringMaterial(floorModule);
            Transform part = stairs.transform.Find(partName);

            if (covering == null || part == null)
            {
                return;
            }

            MeshRenderer renderer = part.GetComponent<MeshRenderer>();

            if (renderer != null && renderer.sharedMaterial != covering)
            {
                renderer.sharedMaterial = covering; // override d'instance, le lien prefab est conserve
            }
        }

        /// <summary>Materiau de la face de marche d'un module de sol (Floor1 = carrelage, Floor2 = parquet, RoofFloor = beton).</summary>
        private static Material FloorCoveringMaterial(string floorModule)
        {
            GameObject prefab = LoadModule(floorModule);

            if (prefab == null)
            {
                return null;
            }

            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name.EndsWith("_Up"))
                {
                    return renderer.sharedMaterial;
                }
            }

            return null;
        }

        /// <summary>Toutes les dalles d'un niveau, sauf celles filtrees (cages d'escalier).</summary>
        private static void FloorTiles(Transform parent, string moduleName, float y, System.Func<int, int, bool> skip)
        {
            for (int i = 0; i < TilesX; i++)
            {
                for (int j = 0; j < TilesZ; j++)
                {
                    if (skip != null && skip(i, j))
                    {
                        continue;
                    }

                    Module(parent, moduleName, TileCenter(i, j, y), 0f);
                }
            }
        }

        /// <summary>
        /// Enveloppe exterieure d'un niveau : angles en Corner_1A, puis un module par
        /// arete de facade choisi par <paramref name="pick"/> (null = arete laissee libre).
        /// </summary>
        private static void Envelope(Transform parent, float y, System.Func<int, int, Side, string> pick)
        {
            Module(parent, "Corner_1A", TileCenter(0, TilesZ - 1, y), 0f);
            Module(parent, "Corner_1A", TileCenter(TilesX - 1, TilesZ - 1, y), 90f);
            Module(parent, "Corner_1A", TileCenter(TilesX - 1, 0, y), 180f);
            Module(parent, "Corner_1A", TileCenter(0, 0, y), 270f);

            for (int i = 1; i < TilesX - 1; i++)
            {
                Facade(parent, i, 0, Side.S, y, pick);
                Facade(parent, i, TilesZ - 1, Side.N, y, pick);
            }

            for (int j = 1; j < TilesZ - 1; j++)
            {
                Facade(parent, 0, j, Side.W, y, pick);
                Facade(parent, TilesX - 1, j, Side.E, y, pick);
            }
        }

        private static void Facade(Transform parent, int i, int j, Side side, float y, System.Func<int, int, Side, string> pick)
        {
            string moduleName = pick(i, j, side);

            if (!string.IsNullOrEmpty(moduleName))
            {
                Module(parent, moduleName, EdgePos(i, j, side, y), Yaw(side));
            }
        }

        // ==================================================================
        // Portes interactives : module du pack demonte, battant sous notre charniere
        // ==================================================================

        private static void Door<T>(Transform wallsParent, Transform doorsParent, string name, int i, int j, Side side, float y, string title, System.Action<T> configure) where T : DoorBase
        {
            BuildDoor<T>(wallsParent, doorsParent, name, "Door_2A", "Interior_Door", -0.45f, i, j, side, y, title, configure);
        }

        private static void ExteriorDoor<T>(Transform wallsParent, Transform doorsParent, string name, int i, int j, Side side, float y, string title, System.Action<T> configure) where T : DoorBase
        {
            BuildDoor<T>(wallsParent, doorsParent, name, "Door_1A", "Exterior_Door", -0.55f, i, j, side, y, title, configure);
        }

        private static void BuildDoor<T>(Transform wallsParent, Transform doorsParent, string name, string moduleName, string leafName, float hingeX,
            int i, int j, Side side, float y, string title, System.Action<T> configure) where T : DoorBase
        {
            Vector3 position = EdgePos(i, j, side, y);
            float yaw = Yaw(side);

            GameObject module = Module(wallsParent, moduleName, position, yaw);

            if (module == null)
            {
                return;
            }

            module.name = moduleName + "_" + name;

            // On casse le lien prefab pour pouvoir sortir le battant du module.
            PrefabUtility.UnpackPrefabInstance(module, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            Transform leaf = module.transform.Find(leafName);

            if (leaf == null)
            {
                Debug.LogError("[Level] Battant '" + leafName + "' introuvable dans " + moduleName);
                return;
            }

            // Racine de la porte : seule la charniere et le battant en dependent,
            // pour que le mur du module ne soit pas pris pour une porte par le raycast.
            GameObject root = new GameObject(name);
            root.transform.SetParent(doorsParent, false);
            root.transform.localPosition = position;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            GameObject hinge = new GameObject("Hinge");
            hinge.transform.SetParent(root.transform, false);
            hinge.transform.localPosition = new Vector3(hingeX, 0f, 0f);

            leaf.SetParent(hinge.transform, true);
            leaf.gameObject.AddComponent<InteractableHighlight>();

            foreach (Transform t in leaf.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            }

            T door = root.AddComponent<T>();
            EditorSetupUtility.SetObjectField(door, "hinge", hinge.transform);

            AudioSource source = root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 1.5f;
            source.maxDistance = 20f;
            source.rolloffMode = AudioRolloffMode.Linear;
            EditorSetupUtility.SetObjectField(door, "audioSource", source);

            if (!string.IsNullOrEmpty(title))
            {
                SetString(door, "displayName", title);
            }

            if (configure != null)
            {
                configure(door);
            }
        }

        // ==================================================================
        // Helpers gameplay
        // ==================================================================

        private static Transform Group(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        internal static GameObject Box(Transform parent, string name, float x0, float x1, float y0, float y1, float z0, float z1, Material material)
        {
            if (x1 - x0 <= 0.0005f || y1 - y0 <= 0.0005f || z1 - z0 <= 0.0005f)
            {
                return null;
            }

            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
            box.transform.localScale = new Vector3(x1 - x0, y1 - y0, z1 - z0);

            if (material != null)
            {
                box.GetComponent<Renderer>().sharedMaterial = material;
            }

            MarkStatic(box);
            return box;
        }

        /// <summary>Volume de piece couvrant les dalles i0..i1 x j0..j1 (inclusives).</summary>
        private static void Room(Transform parent, string roomName, int i0, int i1, int j0, int j1, float y, int floorIndex)
        {
            GameObject go = new GameObject("Room_" + roomName.Replace(' ', '_').Replace('\'', '_'));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = RoomPoint(i0, i1, j0, j1, y, FloorTop + RoomHeight * 0.5f);

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3((i1 - i0 + 1) * Tile, RoomHeight, (j1 - j0 + 1) * Tile);

            RoomVolume room = go.AddComponent<RoomVolume>();
            room.Configure(roomName, floorIndex);
        }

        internal static void Bulb(Transform parent, string name, Vector3 localPosition, Color color, float intensity, float range, bool startOn, bool requiresPower, float failuresPerMinute)
        {
            GameObject bulb = LightSetupMenu.BuildBulb(parent, name, Vector3.zero, color, intensity, range);
            bulb.transform.localPosition = localPosition;

            LightController controller = LightSetupMenu.MakeController(bulb, startOn, requiresPower);
            LightSetupMenu.SetFloat(controller, "randomFailuresPerMinute", failuresPerMinute);
        }

        internal static void Switch(Transform parent, string name, Vector3 localPosition, float autoRadius)
        {
            GameObject sw = LightSetupMenu.BuildSwitch(name, Vector3.zero, new Vector3(0.06f, 0.16f, 0.1f));
            Attach(sw, parent, localPosition);

            LightSwitch component = sw.AddComponent<LightSwitch>();
            LightSetupMenu.SetFloat(component, "autoRadius", autoRadius);
        }

        internal static void Zone(Transform parent, string name, Vector3 localCenter, Vector3 size, string objectiveAsset)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;

            AttachObjective(go, objectiveAsset);
        }

        internal static void Pickup(Transform parent, string itemAsset, Vector3 localPosition, int count)
        {
            ItemData item = LoadItem(itemAsset);

            if (item == null)
            {
                return;
            }

            GameObject prefab = item.WorldPrefab != null ? item.WorldPrefab : ItemSetupMenu.CreateDefaultPickupPrefab();

            if (prefab == null)
            {
                return;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;

            if (instance == null)
            {
                return;
            }

            instance.name = "Pickup_" + item.ItemName;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;

            ItemPickup pickup = instance.GetComponent<ItemPickup>();

            if (pickup != null)
            {
                SerializedObject so = new SerializedObject(pickup);
                so.FindProperty("item").objectReferenceValue = item;
                so.FindProperty("count").intValue = count;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        internal static void AttachObjective(GameObject go, string objectiveAsset)
        {
            ObjectiveData objective = AssetDatabase.LoadAssetAtPath<ObjectiveData>(ObjectivesFolder + "/" + objectiveAsset + ".asset");

            if (objective == null)
            {
                Debug.LogWarning("[Level] Objectif introuvable : " + objectiveAsset);
                return;
            }

            ObjectiveTrigger trigger = EditorSetupUtility.EnsureComponent<ObjectiveTrigger>(go);
            SerializedObject so = new SerializedObject(trigger);
            so.FindProperty("objective").objectReferenceValue = objective;
            so.FindProperty("requireObjectiveActive").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void Attach(GameObject go, Transform parent, Vector3 localPosition)
        {
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
        }

        internal static ItemData LoadItem(string assetName)
        {
            return AssetDatabase.LoadAssetAtPath<ItemData>(ItemsFolder + "/" + assetName + ".asset");
        }

        internal static void SetString(Object target, string field, string value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);

            if (p != null)
            {
                p.stringValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[Level] Champ '" + field + "' introuvable sur " + target.GetType().Name);
            }
        }

        internal static void SetBool(Object target, string field, bool value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);

            if (p != null)
            {
                p.boolValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ==================================================================
        // Materiaux du mobilier provisoire
        // ==================================================================

        internal static void LoadMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Game", "Materials");
            }

            s_Stone = GetOrCreateMaterial("M_House_Stone", new Color(0.36f, 0.36f, 0.38f), 0.1f);
            s_Wood = GetOrCreateMaterial("M_House_Wood", new Color(0.4f, 0.28f, 0.18f), 0.3f);
        }

        private static Material GetOrCreateMaterial(string name, Color color, float smoothness)
        {
            string path = MaterialsFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(shader);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
