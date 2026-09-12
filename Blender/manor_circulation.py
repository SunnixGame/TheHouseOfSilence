# -*- coding: utf-8 -*-
"""
manor_circulation.py — Etape 3 : portes, escaliers et circulation.

Execute par manor_builder.py apres manor_details.py, dans le meme espace de noms.

  1. Placards et armoires : caisson + battants separes (kind "closet", origine au gond,
     hos_swing), marqueur de cachette a l'interieur.
  2. Placard sous le grand escalier (cloisons + porte basse) et trappe du grenier.
  3. Finitions d'escaliers : nez de marche, limons, mains courantes murales.
  4. Verification : hauteur libre au-dessus de chaque marche, obstacles a hauteur de
     tete sur les cellules praticables, largeur de toutes les portes. Les problemes
     sont listes dans le rapport ET materialises par des empties GP_Issue_* pour les
     retrouver dans Blender.

Convention des portes (reprise du builder) : origine sur l'axe des gonds, battant le
long de +X local, `hos_swing` = signe de la rotation Z qui ouvre, `hos_open_angle`.
"""

import math
from collections import deque

import bpy
import mathutils

# Placards / armoires : (niveau, piece, cote du mur, position t, largeur, profondeur)
CLOSETS = [
    ("GF", "Musique", "E", 0.75, 1.2, 0.6),
    ("GF", "Cellier", "W", 0.5, 1.6, 0.6),
    ("GF", "Bureau", "S", 0.25, 1.2, 0.6),
    ("F1", "Chambre2", "W", 0.75, 1.4, 0.65),
    ("F1", "Chambre3", "E", 0.7, 1.2, 0.6),
    ("F1", "ChambreEnfant", "W", 0.5, 1.2, 0.6),
    ("F1", "ChambrePrincipale", "S", 0.65, 1.6, 0.65),
    ("F1", "Placard", "N", 0.5, 2.0, 0.6),
    ("F1", "Dressing", "E", 0.5, 2.4, 0.65),
    ("F1", "ChambreDomestique2", "S", 0.2, 1.2, 0.6),
    ("F2", "Stockage", "S", 0.5, 1.6, 0.6),          # l'armoire qui masque la piece secrete est la porte "secret" voisine
    ("F2", "ChambreAbandonnee1", "W", 0.5, 1.4, 0.6),
    ("F2", "ChambreAbandonnee3", "E", 0.5, 1.4, 0.6),
    ("B1", "Reserve", "W", 0.5, 1.6, 0.6),
]

# Empreintes des escaliers (pour les finitions et la verification)
def stair_definitions(levels):
    B1, GF, F1, F2, TUN, SHED = levels
    return {
        "imperial": dict(x0=22, x1=30, y0=13, y1=19, lane=2.0, landing=2.5, z0=GF.z, z1=F1.z),
        "U": [
            (B1, "EscService", [B1.z, GF.z, F1.z, F2.z], "GF_StairTrim_Service", "GROUND_FLOOR", "M_Wood_Dark", True),
            (B1, "EscSousSol", [B1.z, GF.z], "B1_StairTrim_SousSol", "BASEMENT", "M_Concrete_Wet", False),
            (TUN, "EscSecondaire", [TUN.z, SHED.z], "TUN_StairTrim_Secondaire", "BASEMENT", "M_Concrete_Wet", False),
        ],
        "straight": [
            ((0, 2, 10, 14), B1.z, GF.z, "N", "B1_StairTrim_PassageSecret", "BASEMENT", "M_Wood_Worn", False, ("x", 0.36, 1.815)),
            ((40.8, 44, 22.8, 24), F2.z, F2.ceiling_z, "E", "F2_StairTrim_Grenier", "SECOND_FLOOR", "M_Wood_Worn", True, ("y", 23.92, None)),
            ((48, 51.4, 26, 28), TUN.z, B1.z, "W", "B1_StairTrim_Tunnel", "BASEMENT", "M_Metal_Rust", False, ("y", 26.07, 27.93)),
        ],
    }


# =============================================================================
#  1. PLACARDS ET ARMOIRES
# =============================================================================

def build_closet(level, room_name, side, t, width, depth, index):
    room = level.room(room_name)
    axis, line, a, b, sign, thk = side_frame(room, side, level)
    cx = a + t * (b - a)
    face = sign * thk / 2
    h = 2.2
    z0 = level.z
    acc = MeshAcc()
    # caisson : fond, cotes, dessus, socle, dessous plein (l'interieur reste creux : cachette)
    along_box(acc, axis, line, cx - width / 2, cx + width / 2, face, face + sign * 0.03, z0, z0 + h)
    along_box(acc, axis, line, cx - width / 2, cx - width / 2 + 0.03, face, face + sign * depth, z0, z0 + h)
    along_box(acc, axis, line, cx + width / 2 - 0.03, cx + width / 2, face, face + sign * depth, z0, z0 + h)
    along_box(acc, axis, line, cx - width / 2, cx + width / 2, face, face + sign * depth, z0 + h - 0.04, z0 + h)
    along_box(acc, axis, line, cx - width / 2, cx + width / 2, face, face + sign * depth, z0, z0 + 0.08)
    along_box(acc, axis, line, cx - width / 2 - 0.02, cx + width / 2 + 0.02, face, face + sign * (depth + 0.02), z0 + h, z0 + h + 0.06)
    name = "%s_Closet_%s_%d" % (level.key, room_name, index)
    coll = DETAIL_COLL % level.name
    acc.finish(name, coll, "M_Wood_Dark" if level.key != "F2" else "M_Wood_Worn",
               {"hos_type": "closet", "hos_room": room_name, "hos_level": level.key})

    # deux battants, gonds aux extremites, ouverture vers la piece (loin du mur)
    leaf_w = (width - 0.06) / 2 - 0.005
    leaf_h = h - 0.12
    front = face + sign * depth   # plan des battants
    for k, hinge_u in enumerate((cx - width / 2 + 0.03, cx + width / 2 - 0.03)):
        # direction du battant ferme : du gond vers le milieu
        toward_center = 1.0 if k == 0 else -1.0
        if axis == "x":
            origin = (hinge_u, line + front, z0 + 0.08)
            d = (toward_center, 0.0)
            o = (0.0, float(sign))
        else:
            origin = (line + front, hinge_u, z0 + 0.08)
            d = (0.0, toward_center)
            o = (float(sign), 0.0)
        yaw = math.atan2(d[1], d[0])
        local_y = (-math.sin(yaw), math.cos(yaw))
        swing = 1 if (local_y[0] * o[0] + local_y[1] * o[1]) > 0 else -1
        boxes = [(0.0, leaf_w, -0.02, 0.02, 0.0, leaf_h), (leaf_w - 0.09, leaf_w - 0.05, 0.02 if swing > 0 else -0.06, 0.06 if swing > 0 else -0.02, 1.0, 1.04)]
        props = {"hos_type": "door", "hos_door": "closet", "hos_rooms": "%s | placard" % room_name, "hos_level": level.key,
                 "hos_swing": swing, "hos_opens_into": room_name, "hos_open_angle": 110, "hos_width": leaf_w, "hos_height": leaf_h}
        make_mesh_object("%s_Door_Closet_%s_%d_%s" % (level.key, room_name, index, "L" if k == 0 else "R"), boxes,
                         "%s/Doors" % level.name, "M_Wood_Dark", props=props, origin=origin, rotation_z=yaw)
    # cachette au centre du caisson
    if axis == "x":
        hide = (cx, line + face + sign * depth / 2, z0 + 0.1)
    else:
        hide = (line + face + sign * depth / 2, cx, z0 + 0.1)
    make_empty("GP_Hide_Closet_%s_%s" % (level.key, room_name), "GAMEPLAY_OBJECTS/Hiding_Spots", hide,
               props={"hos_hide": True, "hos_hide_kind": "closet", "hos_level": level.key}, size=0.3)


def build_closets(levels):
    by_key = {lv.key: lv for lv in levels}
    for i, (key, room, side, t, w, d) in enumerate(CLOSETS):
        build_closet(by_key[key], room, side, t, w, d, i + 1)
    return "Placards : %d (2 battants chacun)" % len(CLOSETS)


# =============================================================================
#  2. PLACARD SOUS LE GRAND ESCALIER, TRAPPE DU GRENIER
# =============================================================================

def build_special_doors(levels):
    B1, GF, F1, F2, TUN, SHED = levels
    z_mid = (GF.z + F1.z) / 2
    # Cloisons sous le palier intermediaire (y 16.5..19) : cotes ouest (perce d'une porte
    # basse 0.8 x 1.7), est, et devant sous la volee centrale.
    acc = MeshAcc()
    acc.box(22.0, 22.12, 16.5, 17.3, GF.z, z_mid - 0.2)
    acc.box(22.0, 22.12, 18.1, 19.0, GF.z, z_mid - 0.2)
    acc.box(22.0, 22.12, 17.3, 18.1, GF.z + 1.85, z_mid - 0.2)
    acc.box(29.88, 30.0, 16.5, 19.0, GF.z, z_mid - 0.2)
    acc.box(22.0, 30.0, 16.5, 16.62, GF.z, z_mid - 0.2)
    acc.finish("GF_UnderStair_Walls", DETAIL_COLL % "GROUND_FLOOR", "M_Wood_Dark", {"hos_type": "closet", "hos_level": "GF"})
    # gond au nord (y 18.1), battant ferme vers le sud (lacet -90) ; local +Y = +X monde = vers
    # l'interieur du placard, donc swing -1 ouvre vers le hall (bande ouest, 2 m de large).
    props = {"hos_type": "door", "hos_door": "closet", "hos_rooms": "Hall | sous escalier", "hos_level": "GF",
             "hos_swing": -1, "hos_opens_into": "Hall", "hos_open_angle": 100, "hos_width": 0.8, "hos_height": 1.85}
    make_mesh_object("GF_Door_Hall_SousEscalier", door_leaf_boxes("single", 0.8, 1.85), "GROUND_FLOOR/Doors", "M_Wood_Dark",
                     props=props, origin=(22.06, 18.1, GF.z), rotation_z=-math.pi / 2)
    make_empty("GP_Door_Hall_SousEscalier", "GAMEPLAY_OBJECTS/Door_Points", (22.06, 17.7, GF.z + 0.85), props=props, size=0.3)

    # Trappe du grenier au sommet de l'echelle (plafond du 2e a 10.6), gond sur le bord nord
    trap = MeshAcc()
    trap.box(42.8, 44.0, 22.95, 23.98, F2.ceiling_z - 0.05, F2.ceiling_z + 0.0)
    trap_obj = trap.finish("F2_Door_Trappe_Grenier", "SECOND_FLOOR/Doors", "M_Wood_Worn",
                           {"hos_type": "door", "hos_door": "trapdoor", "hos_rooms": "EscGrenier | toit", "hos_level": "F2",
                            "hos_swing": 1, "hos_open_angle": 80, "hos_width": 1.2, "hos_height": 0.6})
    # origine sur l'axe des gonds (bord nord), rotation X ouvre vers le haut
    trap_obj.data.transform(mathutils.Matrix.Translation((-43.4, -23.98, -F2.ceiling_z)))
    trap_obj.location = (43.4, 23.98, F2.ceiling_z)
    make_empty("GP_Door_Trappe_Grenier", "GAMEPLAY_OBJECTS/Door_Points", (43.4, 23.5, F2.ceiling_z - 0.3),
               props={"hos_door": "trapdoor", "hos_level": "F2", "hos_swing": 1}, size=0.3)
    # cadre de trappe dans le plafond
    fr = MeshAcc()
    fr.box(42.7, 44.0, 22.85, 22.95, F2.ceiling_z - 0.08, F2.ceiling_z + SLAB)
    fr.box(42.7, 44.0, 23.98, 24.05, F2.ceiling_z - 0.08, F2.ceiling_z + SLAB)
    fr.box(42.7, 42.8, 22.85, 24.05, F2.ceiling_z - 0.08, F2.ceiling_z + SLAB)
    fr.finish("F2_Trappe_Cadre", DETAIL_COLL % "SECOND_FLOOR", "M_Wood_Worn", {"hos_type": "frame", "hos_level": "F2"})

    # Rampe de collision invisible sur l'echelle du grenier : ses girons de 0.2 m font
    # buter un CharacterController, la rampe (41 degres < slopeLimit 55) le laisse monter.
    ramp = MeshAcc()
    ramp.bar((40.8, 23.4), (43.8, 23.4), F2.z + 0.175, F2.ceiling_z, 1.2, 0.05)
    ramp.finish("F2_Stairs_Grenier_Ramp", "SECOND_FLOOR/Stairs", "M_Wood_Worn",
                {"hos_type": "stairs_ramp", "hos_level": "F2", "hos_collider_only": True})
    return "Placard sous escalier + trappe du grenier + rampe de collision de l'echelle"


# =============================================================================
#  3. FINITIONS D'ESCALIERS
# =============================================================================

def nosings_from_boxes(acc, boxes, direction):
    """Nez de marche : petite boite au bord avant de chaque marche."""
    for (x0, x1, y0, y1, z0, z1) in boxes:
        if direction == "N":
            acc.box(x0, x1, y0 - 0.03, y0 + 0.025, z1 - 0.035, z1 + 0.005)
        elif direction == "S":
            acc.box(x0, x1, y1 - 0.025, y1 + 0.03, z1 - 0.035, z1 + 0.005)
        elif direction == "E":
            acc.box(x0 - 0.03, x0 + 0.025, y0, y1, z1 - 0.035, z1 + 0.005)
        else:
            acc.box(x1 - 0.025, x1 + 0.03, y0, y1, z1 - 0.035, z1 + 0.005)


def stringers(acc, lane, y_start, y_end, z_start, z_end, along="y"):
    """Limons : deux planches inclinees sur les bords de la volee."""
    for u in (lane[0] + 0.03, lane[1] - 0.03):
        if along == "y":
            acc.bar((u, y_start), (u, y_end), z_start - 0.22, z_end - 0.22, 0.05, 0.28)
        else:
            acc.bar((y_start, u), (y_end, u), z_start - 0.22, z_end - 0.22, 0.05, 0.28)


def wall_handrail(acc, p0, p1, z0, z1, height=0.9):
    """Main courante fixee au mur : barre + consoles tous les ~1 m."""
    acc.bar(p0, p1, z0 + height, z1 + height, 0.05, 0.05)
    L = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
    n = max(1, int(L / 1.0))
    for i in range(n + 1):
        t = i / n
        x, y = p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t
        z = z0 + (z1 - z0) * t + height
        acc.box(x - 0.025, x + 0.025, y - 0.025, y + 0.025, z - 0.08, z)


def build_stair_trims(levels):
    defs = stair_definitions(levels)
    B1, GF, F1, F2, TUN, SHED = levels
    made = 0

    # --- Escalier imperial : nez de marche + limons des trois volees
    d = defs["imperial"]
    z_mid = (d["z0"] + d["z1"]) / 2
    y_land = d["y1"] - d["landing"]
    acc = MeshAcc()
    center = (d["x0"] + d["lane"], d["x1"] - d["lane"])
    f, n, riser, tread = flight_boxes(center, d["y0"], y_land, d["z0"], z_mid, True, 0.3)
    nosings_from_boxes(acc, f, "N")
    stringers(acc, center, d["y0"], y_land, d["z0"], z_mid)
    for lane in ((d["x0"], d["x0"] + d["lane"]), (d["x1"] - d["lane"], d["x1"])):
        f, n, riser, tread = flight_boxes(lane, y_land, d["y0"], z_mid, d["z1"], False, 0.3)
        nosings_from_boxes(acc, f, "S")
        stringers(acc, lane, y_land, d["y0"], z_mid, d["z1"])
    acc.finish("GF_StairTrim_GrandEscalier", "GROUND_FLOOR/Stairs", "M_Wood_Dark", {"hos_type": "stair_trim"})
    made += 1

    # --- Escaliers en U : nez, limons (bois) et mains courantes murales sur les cotes exterieurs
    for level_bottom, room_name, z_levels, name, coll_prefix, mat, wood in defs["U"]:
        r = level_bottom.room(room_name)
        W = r.x1 - r.x0
        lane_w = (W - 0.1) / 2
        lane1, lane2 = (r.x0, r.x0 + lane_w), (r.x1 - lane_w, r.x1)
        yf0, yf1 = r.y0 + ARRIVAL, r.y1 - LANDING
        acc = MeshAcc()
        for za, zb in zip(z_levels[:-1], z_levels[1:]):
            zm = (za + zb) / 2
            f1, n1, riser, tread = flight_boxes(lane1, yf0, yf1, za, zm, True)
            f2, n2, _, _ = flight_boxes(lane2, yf1, yf0, zm, zb, False)
            nosings_from_boxes(acc, f1, "N")
            nosings_from_boxes(acc, f2, "S")
            if wood:
                stringers(acc, lane1, yf0, yf1, za, zm)
                stringers(acc, lane2, yf1, yf0, zm, zb)
            wall_handrail(acc, (r.x0 + 0.08, yf0), (r.x0 + 0.08, yf1), za, zm)
            wall_handrail(acc, (r.x1 - 0.08, yf1), (r.x1 - 0.08, yf0), zm, zb)
            wall_handrail(acc, (r.x0 + 0.5, r.y1 - 0.08), (r.x1 - 0.5, r.y1 - 0.08), zm, zm)
        acc.finish(name, "%s/Stairs" % coll_prefix, mat, {"hos_type": "stair_trim"})
        made += 1

    # --- Volees droites : nez + mains courantes sur les deux murs de la cage
    for rect, z0, z1, direction, name, coll_prefix, mat, wood, (wall_axis, wa, wb) in defs["straight"]:
        x0, x1, y0, y1 = rect
        acc = MeshAcc()
        if direction in ("N", "S"):
            lane = (x0, x1)
            north = direction == "N"
            f, n, riser, tread = flight_boxes(lane, y0 if north else y1, y1 if north else y0, z0, z1, north)
            nosings_from_boxes(acc, f, direction)
            if wood:
                stringers(acc, lane, y0 if north else y1, y1 if north else y0, z0, z1)
            for wx in (wa, wb):
                if wx is None:
                    continue
                ya, yb = (y0, y1) if north else (y1, y0)
                wall_handrail(acc, (wx, ya), (wx, yb), z0, z1)
        else:
            lane = (y0, y1)
            east = direction == "E"
            f, n, riser, tread = flight_boxes(lane, x0 if east else x1, x1 if east else x0, z0, z1, east)
            f = [(b[2], b[3], b[0], b[1], b[4], b[5]) for b in f]
            nosings_from_boxes(acc, f, direction)
            for wy in (wa, wb):
                if wy is None:
                    continue
                xa, xb = (x0, x1) if east else (x1, x0)
                wall_handrail(acc, (xa, wy), (xb, wy), z0, z1)
            if wb is None:
                # cote ouvert : garde-corps le long de la volee
                railing(acc, (x0, y0 + 0.06), (x1, y0 + 0.06), z0, z1, height=0.95)
        acc.finish(name, "%s/Stairs" % coll_prefix, mat, {"hos_type": "stair_trim"})
        made += 1
    return "Finitions d'escaliers : %d objets (nez de marche, limons, mains courantes)" % made


# =============================================================================
#  4. VERIFICATION DE CIRCULATION
# =============================================================================

def _boxes_of(obj):
    mw = obj.matrix_world
    vs = [mw @ v.co for v in obj.data.vertices]
    out = []
    for i in range(0, len(vs) - 7, 8):
        c = vs[i:i + 8]
        out.append((min(p.x for p in c), max(p.x for p in c), min(p.y for p in c), max(p.y for p in c), min(p.z for p in c), max(p.z for p in c)))
    return out


def verify_circulation(levels):
    issues = []
    # collections d'issues
    get_collection("GAMEPLAY_OBJECTS/Issues")

    # --- a) hauteur libre au-dessus des marches (rayon vers le haut depuis chaque marche)
    dg = bpy.context.evaluated_depsgraph_get()
    scene = bpy.context.scene
    min_head = {}
    for obj in bpy.data.objects:
        if obj.get("hos_type") != "stairs":
            continue
        worst = (99.0, None)
        for (x0, x1, y0, y1, z0, z1) in _boxes_of(obj):
            if (x1 - x0) * (y1 - y0) > 6.0:      # palier : on teste aussi mais au centre seulement
                pts = [((x0 + x1) / 2, (y0 + y1) / 2)]
            else:
                pts = [((x0 + x1) / 2, (y0 + y1) / 2)]
            for (px, py) in pts:
                origin = mathutils.Vector((px, py, z1 + 0.05))
                ok, loc, nrm, idx, hit, mw = scene.ray_cast(dg, origin, mathutils.Vector((0, 0, 1)), distance=6.0)
                clear = (loc.z - z1) if ok else 6.0
                if hit is obj and ok:
                    # la marche suivante du meme objet (volee superposee) compte comme obstacle : garde
                    pass
                if clear < worst[0]:
                    worst = (clear, (round(px, 2), round(py, 2), round(z1, 2), hit.name if ok else None))
        min_head[obj.name] = worst
        if worst[0] < 2.0 and "Grenier" not in obj.name:      # l'echelle du grenier bute volontairement sur la trappe
            issues.append("Hauteur libre %.2f m sur %s en %s (obstacle %s)" % (worst[0], obj.name, worst[1][:3], worst[1][3]))
            make_empty("GP_Issue_Headroom_%s" % obj.name, "GAMEPLAY_OBJECTS/Issues", (worst[1][0], worst[1][1], worst[1][2] + 1.0),
                       props={"hos_issue": "headroom", "hos_value": worst[0]}, size=0.5)

    # --- b) obstacles a hauteur de tete (1.5..1.85) sur les cellules libres au corps (0.3..1.5)
    walls, floors = [], []
    solid_types = {"wall_int", "wall_ext", "column", "fireplace", "vault", "closet", "beam"}
    for obj in bpy.data.objects:
        t = obj.get("hos_type")
        if t in ("wall_int", "wall_ext"):
            walls.extend(_boxes_of(obj))                 # boites exactes (8 sommets par boite)
        elif t in solid_types:
            mw = obj.matrix_world                        # meshes fusionnes : bbox de chaque face
            for poly in obj.data.polygons:
                vs = [mw @ obj.data.vertices[i].co for i in poly.vertices]
                walls.append((min(v.x for v in vs), max(v.x for v in vs), min(v.y for v in vs), max(v.y for v in vs),
                              min(v.z for v in vs), max(v.z for v in vs)))
        elif t in ("floor", "stairs"):
            floors.extend(_boxes_of(obj))
    level_z = {lv.key: lv.z for lv in levels}
    spawn = {"B1": (26, 22.5), "GF": (26, 6), "F1": (26, 2), "F2": (26, 20.2), "TUN": (60, 27), "SHED": (92, 36.5)}
    cell, R = 0.25, 0.3
    head_hits = {}
    unreachable = {}
    markers = [o for o in bpy.data.objects if o.name.startswith("GP_Nav_")]
    for key, z0 in level_z.items():
        body = (z0 + 0.3, z0 + 1.5)
        head = (z0 + 1.5, z0 + 1.85)
        xs0, xs1, ys0, ys1 = (-2, 96, -8, 50)
        nx, ny = int((xs1 - xs0) / cell), int((ys1 - ys0) / cell)
        lw = [b for b in walls if b[4] < body[1] and b[5] > body[0]]
        lh = [b for b in walls if b[4] < head[1] and b[5] > head[0]]
        lf = [b for b in floors if abs(b[5] - z0) < 0.06]
        free = [[False] * ny for _ in range(nx)]
        for i in range(nx):
            px = xs0 + (i + 0.5) * cell
            for j in range(ny):
                py = ys0 + (j + 0.5) * cell
                if not any(b[0] <= px <= b[1] and b[2] <= py <= b[3] for b in lf):
                    continue
                free[i][j] = not any(b[0] - R <= px <= b[1] + R and b[2] - R <= py <= b[3] + R for b in lw)
        si, sj = int((spawn[key][0] - xs0) / cell), int((spawn[key][1] - ys0) / cell)
        seen = [[False] * ny for _ in range(nx)]
        q = deque([(si, sj)])
        seen[si][sj] = True
        reached = []
        while q:
            i, j = q.popleft()
            reached.append((i, j))
            for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                a, b = i + di, j + dj
                if 0 <= a < nx and 0 <= b < ny and not seen[a][b] and free[a][b]:
                    seen[a][b] = True
                    q.append((a, b))
        hits = []
        for (i, j) in reached:
            px, py = xs0 + (i + 0.5) * cell, ys0 + (j + 0.5) * cell
            if any(b[0] - R <= px <= b[1] + R and b[2] - R <= py <= b[3] + R for b in lh):
                hits.append((round(px, 2), round(py, 2)))
        head_hits[key] = hits
        for k, (px, py) in enumerate(hits[:6]):
            make_empty("GP_Issue_Head_%s_%d" % (key, k + 1), "GAMEPLAY_OBJECTS/Issues", (px, py, z0 + 1.7), props={"hos_issue": "head"}, size=0.4)
        unreachable[key] = []
        for m in markers:
            if m.get("hos_level") != key:
                continue
            mi, mj = int((m.location.x - xs0) / cell), int((m.location.y - ys0) / cell)
            if not any(0 <= mi + a < nx and 0 <= mj + b < ny and seen[mi + a][mj + b] for a in range(-4, 5) for b in range(-4, 5)):
                unreachable[key].append(m.get("hos_room"))
        if hits:
            issues.append("%s : %d cellules libres au corps mais obstacle a hauteur de tete (ex. %s)" % (key, len(hits), hits[:3]))
        if unreachable[key]:
            issues.append("%s : pieces non atteignables %s" % (key, unreachable[key]))

    # --- c) largeur et hauteur des portes
    narrow = [(o.name, o.get("hos_width"), o.get("hos_height")) for o in bpy.data.objects
              if o.get("hos_type") == "door" and o.get("hos_door") not in ("closet", "trapdoor")
              and (o.get("hos_width", 9) < 0.85 or o.get("hos_height", 9) < 1.9)]
    if narrow:
        issues.append("Portes etroites ou basses : %s" % narrow)

    doors = sum(1 for o in bpy.data.objects if o.get("hos_type") == "door")
    return issues, dict(doors=doors, headroom={k: round(v[0], 2) for k, v in min_head.items()}, head_hits={k: len(v) for k, v in head_hits.items()},
                        unreachable=unreachable)


# =============================================================================
#  POINT D'ENTREE
# =============================================================================

def build_circulation(levels):
    reports = []
    reports.append(build_closets(levels))
    reports.append(build_special_doors(levels))
    reports.append(build_stair_trims(levels))
    issues, stats = verify_circulation(levels)
    reports.append("Verification : %d portes, hauteur libre mini par escalier %s, tetes %s, non atteignables %s" %
                   (stats["doors"], stats["headroom"], stats["head_hits"], stats["unreachable"]))
    for issue in issues:
        reports.append("ISSUE " + issue)
    return reports
