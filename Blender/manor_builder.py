# -*- coding: utf-8 -*-
"""
manor_builder.py — Blockout procedural du manoir "House of Silence" pour Unity 6.

Etape 1 : volumes principaux (murs epais, dalles, plafonds, tremies, escaliers en
vraies marches, ouvertures de portes / fenetres, battants avec origine au gond,
toiture simple, empties de gameplay). Pas de details fins ni de textures : ils
arrivent aux etapes 2 et 3.

Tout le manoir est decrit par des TABLES en bas de ce fichier (LEVELS) :
    - une piece = rectangle (x0, x1, y0, y1) en metres, X vers l'est, Y vers le nord,
      origine a l'angle sud-ouest du rez-de-chaussee ;
    - les murs sont deduits des aretes des pieces (arete partagee = cloison,
      arete libre = mur exterieur), epaisseur selon le niveau ;
    - une porte relie deux pieces (ou une piece et l'exterieur) a une position t
      (0..1) le long de leur arete commune ; une fenetre = piece + cote + positions ;
    - un escalier = type + piece(s) + niveaux desservis.

Modifier le manoir = modifier les tables. Relancer le script regenere tout
(la collection MANOR_ROOT est supprimee puis reconstruite).

Conventions Unity : le fichier est en metres, Y Blender = Z Unity (l'export FBX
-Z forward / Y up fait la conversion). Les portes ont l'origine sur l'axe de la
charniere, battant le long de +X local : une rotation Z positive ouvre la porte.

Usage dans Blender :  exec(open(r"...\\manor_builder.py", encoding="utf-8").read())
"""

import math
import os

import bpy

# =============================================================================
#  PARAMETRES GLOBAUX
# =============================================================================

SLAB = 0.2            # epaisseur d'une dalle de sol ET d'un plafond (0.4 au total entre niveaux)
GROUND_Z = -1.4       # niveau du terrain autour du manoir (le rez est sureleve)
STEP_RISE_MAX = 0.18  # hauteur de marche maximale
LANDING = 1.8         # profondeur de palier des escaliers en U
ARRIVAL = 1.0         # bande d'arrivee en haut d'un escalier en U

BLEND_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else bpy.path.abspath("//"),
                          "Manor_Blockout.blend")

# Dimensions des ouvertures : (largeur, hauteur, a un battant ?)
DOOR_KINDS = {
    "single":    (0.95, 2.10, True),
    "service":   (0.90, 2.00, True),
    "double":    (1.80, 2.40, True),
    "entrance":  (2.00, 2.80, True),
    "opening":   (1.80, 2.60, False),   # baie libre sans battant
    "secret":    (0.90, 2.00, True),    # porte cachee (rayonnage, casier, panneau)
    "bars":      (0.90, 2.00, True),    # grille de cellule
    "breakable": (1.00, 2.00, True),    # cloison a casser (pied-de-biche)
    "hatch":     (0.90, 1.90, True),    # portillon des combles (praticable sans s'accroupir)
    "closet":    (0.55, 2.00, True),    # battant de placard / armoire
    "metal":     (1.00, 2.00, True),    # porte metallique (morgue, tunnel)
}

# Fenetres par niveau : (hauteur d'allege, hauteur de la fenetre, largeur)
WINDOW_KINDS = {
    "GF":   (0.9, 2.6, 1.4),
    "F1":   (0.9, 2.0, 1.2),
    "F2":   (0.55, 0.7, 0.9),   # petites fenetres de comble sous l'egout
    "B1":   (2.2, 0.6, 0.8),   # soupiraux
    "SHED": (1.2, 0.6, 0.8),
    "TUN":  (1.2, 0.6, 0.8),
}

MATERIALS = {
    "M_Stone_Old":     (0.42, 0.40, 0.36),
    "M_Wood_Dark":     (0.20, 0.12, 0.07),
    "M_Wood_Worn":     (0.36, 0.27, 0.17),
    "M_Plaster_Old":   (0.72, 0.68, 0.58),
    "M_Wallpaper_Old": (0.45, 0.38, 0.30),
    "M_Metal_Rust":    (0.35, 0.20, 0.12),
    "M_WoodFloor":     (0.33, 0.22, 0.12),
    "M_Tile_Old":      (0.55, 0.52, 0.45),
    "M_Concrete_Wet":  (0.25, 0.26, 0.25),
    "M_Roof_Tiles":    (0.22, 0.15, 0.13),
    "M_Glass_Dirty":   (0.55, 0.65, 0.65),
    "M_Ground":        (0.12, 0.16, 0.10),
}

# =============================================================================
#  UTILITAIRES BLENDER
# =============================================================================

_collections = {}
_materials = {}


def get_material(name):
    mat = _materials.get(name) or bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        rgb = MATERIALS.get(name, (0.5, 0.5, 0.5))
        mat.diffuse_color = (*rgb, 1.0)
        for node in mat.node_tree.nodes:
            if node.type == "BSDF_PRINCIPLED":
                node.inputs["Base Color"].default_value = (*rgb, 1.0)
                node.inputs["Roughness"].default_value = 0.85
                if name == "M_Glass_Dirty":
                    node.inputs["Roughness"].default_value = 0.3
    _materials[name] = mat
    return mat


SHORT = {"EXTERIOR": "EXT", "GROUND_FLOOR": "GF", "FIRST_FLOOR": "F1", "SECOND_FLOOR": "F2", "BASEMENT": "B1",
         "GAMEPLAY_OBJECTS": "GP"}


def get_collection(path):
    """Cree (ou retrouve) une collection imbriquee 'A/B/C' sous la scene.
    Les sous-collections de niveau 3 recoivent un prefixe (GF_Walls, F1_Walls...)
    car Blender impose des noms de collections uniques."""
    if path in _collections:
        return _collections[path]
    parent = bpy.context.scene.collection
    full = ""
    parts = path.split("/")
    for depth, part in enumerate(parts):
        full = part if not full else full + "/" + part
        if full in _collections:
            parent = _collections[full]
            continue
        if parent is bpy.context.scene.collection or parent.name == "MANOR_ROOT":
            coll_name = part
        else:
            coll_name = SHORT.get(parent.name, parent.name) + "_" + part
        coll = bpy.data.collections.new(coll_name)
        parent.children.link(coll)
        _collections[full] = coll
        parent = coll
    return parent


def clear_previous():
    root = bpy.data.collections.get("MANOR_ROOT")
    if root is None:
        return

    def gather(coll, acc):
        acc.append(coll)
        for c in coll.children:
            gather(c, acc)
        return acc

    for coll in gather(root, []):
        for obj in list(coll.objects):
            mesh = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            if mesh is not None and isinstance(mesh, bpy.types.Mesh) and mesh.users == 0:
                bpy.data.meshes.remove(mesh)
    for coll in reversed(gather(root, [])):
        bpy.data.collections.remove(coll)


def box_geometry(x0, x1, y0, y1, z0, z1, offset=0):
    """Sommets / faces d'une boite, normales vers l'exterieur."""
    v = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
         (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (3, 0, 4, 7), (1, 2, 6, 5)]
    return v, [tuple(i + offset for i in face) for face in f]


def make_mesh_object(name, boxes, coll_path, material, props=None, origin=(0.0, 0.0, 0.0), rotation_z=0.0):
    """Cree un objet dont le mesh est l'union (non fusionnee) de plusieurs boites.

    Les boites sont donnees en coordonnees LOCALES a l'objet ; 'origin' place
    l'objet dans le monde. Les murs / dalles utilisent origin = (0,0,0) et des
    coordonnees monde directement.
    """
    verts, faces = [], []
    for b in boxes:
        v, f = box_geometry(*b, offset=len(verts))
        verts.extend(v)
        faces.extend(f)
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    obj.location = origin
    obj.rotation_euler = (0.0, 0.0, rotation_z)
    if material:
        mesh.materials.append(get_material(material))
    get_collection(coll_path).objects.link(obj)
    if props:
        for k, val in props.items():
            obj[k] = val
    return obj


def make_poly_object(name, verts, faces, coll_path, material, props=None):
    """Objet a partir de sommets / faces arbitraires ; les normales sont recalculees vers l'exterieur."""
    import bmesh
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    if material:
        mesh.materials.append(get_material(material))
    get_collection(coll_path).objects.link(obj)
    if props:
        for k, val in props.items():
            obj[k] = val
    return obj


def make_empty(name, coll_path, location, props=None, size=0.5):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = "SPHERE"
    obj.empty_display_size = size
    obj.location = location
    get_collection(coll_path).objects.link(obj)
    if props:
        for k, val in props.items():
            obj[k] = val
    return obj


# =============================================================================
#  MODELE DE DONNEES
# =============================================================================

class Room:
    def __init__(self, name, x0, x1, y0, y1, zone=None, floor=True, ceiling=True, floor_mat=None, wall_mat=None):
        self.name = name
        self.x0, self.x1, self.y0, self.y1 = float(x0), float(x1), float(y0), float(y1)
        self.zone = zone            # deux pieces de meme zone ne sont pas separees par un mur
        self.floor = floor          # False = tremie (vide, cage d'escalier)
        self.ceiling = ceiling
        self.floor_mat = floor_mat
        self.wall_mat = wall_mat

    @property
    def rect(self):
        return (self.x0, self.x1, self.y0, self.y1)

    @property
    def center(self):
        return ((self.x0 + self.x1) * 0.5, (self.y0 + self.y1) * 0.5)

    def edge(self, side):
        """(axe, ligne, a, b) : axe 'x' = mur le long de X (ligne = y constant)."""
        if side == "S":
            return ("x", self.y0, self.x0, self.x1)
        if side == "N":
            return ("x", self.y1, self.x0, self.x1)
        if side == "W":
            return ("y", self.x0, self.y0, self.y1)
        return ("y", self.x1, self.y0, self.y1)


class Level:
    def __init__(self, key, name, z, height, ext_thk, int_thk, wall_mat, ext_mat, floor_mat, ceil_mat, stair_mat, top=False):
        self.key = key
        self.name = name
        self.z = float(z)                 # sol fini
        self.height = float(height)       # sous plafond
        self.ext_thk = ext_thk
        self.int_thk = int_thk
        self.wall_mat = wall_mat
        self.ext_mat = ext_mat
        self.floor_mat = floor_mat
        self.ceil_mat = ceil_mat
        self.stair_mat = stair_mat
        self.top = top                    # dernier niveau : les murs exterieurs s'arretent au plafond
        self.wall_bottom = None           # base des murs exterieurs (defaut : sous la dalle)
        self.ext_top = None               # sommet des murs exterieurs (defaut : plafond + dalle, ou plafond si top)
        self.slope_run = 0.0              # combles : longueur horizontale du rampant depuis le mur exterieur
        self.pieces = []                  # troncons de murs elementaires (remplis par build_walls)
        self.openings = []                # ouvertures resolues (idem)
        self.rooms = []
        self.doors = []                   # (roomA, roomB|"EXT:S", t, kind)
        self.windows = []                 # (room, side, [t...])
        self.above = []                   # niveaux situes au-dessus (pour les tremies du plafond)

    def room(self, name):
        for r in self.rooms:
            if r.name == name:
                return r
        raise KeyError("%s : piece inconnue '%s'" % (self.key, name))

    @property
    def ceiling_z(self):
        return self.z + self.height


# =============================================================================
#  GEOMETRIE : MURS
# =============================================================================

def rect_overlap_1d(a0, a1, b0, b1):
    lo, hi = max(a0, b0), min(a1, b1)
    return (lo, hi) if hi - lo > 1e-6 else None


def shared_edge(ra, rb):
    """Arete commune de deux pieces : (axe, ligne, a, b) ou None."""
    for side_a, side_b in (("N", "S"), ("S", "N"), ("E", "W"), ("W", "E")):
        axis, line, a0, a1 = ra.edge(side_a)
        axis_b, line_b, b0, b1 = rb.edge(side_b)
        if abs(line - line_b) < 1e-6:
            ov = rect_overlap_1d(a0, a1, b0, b1)
            if ov:
                return (axis, line, ov[0], ov[1])
    return None


def compute_wall_runs(level):
    """Decoupe toutes les aretes de pieces en troncons elementaires puis les
    fusionne en 'runs' (meme ligne, meme type). Retourne une liste de dicts."""
    lines = {}   # (axis, line) -> list of (a, b, room, orientation)  orientation: +1 piece du cote positif de la ligne

    for r in level.rooms:
        for side in ("S", "N", "W", "E"):
            axis, line, a, b = r.edge(side)
            orient = +1 if side in ("S", "W") else -1   # 'S' : la piece est au nord (cote +) de sa ligne sud
            lines.setdefault((axis, round(line, 4)), []).append((a, b, r, orient))

    pieces = []
    for (axis, line), edges in lines.items():
        cuts = sorted(set([e[0] for e in edges] + [e[1] for e in edges]))
        for a, b in zip(cuts[:-1], cuts[1:]):
            pos = [e[2] for e in edges if e[0] <= a + 1e-6 and e[1] >= b - 1e-6 and e[3] > 0]
            neg = [e[2] for e in edges if e[0] <= a + 1e-6 and e[1] >= b - 1e-6 and e[3] < 0]
            if not pos and not neg:
                continue
            if pos and neg:
                ra, rb = neg[0], pos[0]
                if ra.zone is not None and ra.zone == rb.zone:
                    continue   # meme zone : pas de mur (balcon autour du vide, tunnel...)
                pieces.append(dict(axis=axis, line=line, a=a, b=b, ext=False, rooms=(ra.name, rb.name), neg=ra, pos=rb))
            else:
                only = (pos or neg)[0]
                pieces.append(dict(axis=axis, line=line, a=a, b=b, ext=True, rooms=(only.name,),
                                   neg=only if neg else None, pos=only if pos else None))

    # fusion des troncons contigus de meme nature
    pieces.sort(key=lambda p: (p["axis"], p["line"], p["a"]))
    runs = []
    for p in pieces:
        if runs:
            q = runs[-1]
            if q["axis"] == p["axis"] and abs(q["line"] - p["line"]) < 1e-6 and abs(q["b"] - p["a"]) < 1e-6 and q["ext"] == p["ext"]:
                q["b"] = p["b"]
                q["rooms"] = tuple(sorted(set(q["rooms"]) | set(p["rooms"])))
                continue
        runs.append(dict(p, openings=[]))
    level.pieces = pieces
    return runs


def resolve_opening(level, spec):
    """Porte : (A, B, t, kind) -> (axis, line, center, width, bottom, top, kind, A, B)."""
    a_name, b_name, t, kind = spec
    ra = level.room(a_name)
    if b_name.startswith("EXT:"):
        axis, line, a, b = ra.edge(b_name[4:])
    else:
        rb = level.room(b_name)
        e = shared_edge(ra, rb)
        if e is None:
            raise ValueError("%s : %s et %s n'ont pas d'arete commune" % (level.key, a_name, b_name))
        axis, line, a, b = e
    w, h, _leaf = DOOR_KINDS[kind]
    center = a + t * (b - a)
    return dict(axis=axis, line=line, center=center, width=w, bottom=level.z, top=level.z + h, kind=kind, a=a_name, b=b_name)


def resolve_window(level, spec):
    room_name, side, ts = spec
    r = level.room(room_name)
    axis, line, a, b = r.edge(side)
    sill, h, w = WINDOW_KINDS[level.key]
    return [dict(axis=axis, line=line, center=a + t * (b - a), width=w, bottom=level.z + sill, top=level.z + sill + h,
                 kind="window", a=room_name, b="EXT", side=side) for t in ts]


def attach_openings(runs, openings, level):
    for op in openings:
        for run in runs:
            if run["axis"] == op["axis"] and abs(run["line"] - op["line"]) < 1e-6 and run["a"] - 1e-6 <= op["center"] <= run["b"] + 1e-6:
                lo, hi = op["center"] - op["width"] / 2, op["center"] + op["width"] / 2
                if lo < run["a"] - 1e-6 or hi > run["b"] + 1e-6:
                    print("[manor] ATTENTION %s : ouverture %s-%s deborde du mur (%.1f..%.1f)" % (level.key, op["a"], op["b"], run["a"], run["b"]))
                if op["kind"] == "window" and not run["ext"]:
                    print("[manor] ATTENTION %s : fenetre de %s cote %s sur un mur INTERIEUR, ignoree" % (level.key, op["a"], op.get("side")))
                    op["skip"] = True
                    break
                for other in run["openings"]:
                    o_lo, o_hi = other["center"] - other["width"] / 2, other["center"] + other["width"] / 2
                    if lo < o_hi and hi > o_lo:
                        print("[manor] ATTENTION %s : ouvertures %s-%s et %s-%s se chevauchent" % (level.key, op["a"], op["b"], other["a"], other["b"]))
                op["ext"] = run["ext"]
                op["thk"] = level.ext_thk if run["ext"] else level.int_thk
                op["run_a"], op["run_b"] = run["a"], run["b"]
                run["openings"].append(op)
                break
        else:
            print("[manor] ATTENTION %s : aucun mur pour l'ouverture %s-%s" % (level.key, op["a"], op["b"]))


def build_walls(level, prefix):
    runs = compute_wall_runs(level)
    openings = [resolve_opening(level, d) for d in level.doors]
    for w in level.windows:
        openings.extend(resolve_window(level, w))
    attach_openings(runs, openings, level)

    z_bottom_int, z_top_int = level.z, level.ceiling_z
    z_bottom_ext = level.wall_bottom if level.wall_bottom is not None else level.z - 2 * SLAB
    z_top_ext = level.ceiling_z if level.top else level.ceiling_z + 2 * SLAB
    if level.ext_top is not None:
        z_top_ext = level.ext_top
    level.openings = openings

    counter = {}
    for run in runs:
        thk = level.ext_thk if run["ext"] else level.int_thk
        z0, z1 = (z_bottom_ext, z_top_ext) if run["ext"] else (z_bottom_int, z_top_int)
        half = thk / 2
        a, b = run["a"] - half, run["b"] + half     # prolonge aux angles pour fermer les coins
        ops = sorted(run["openings"], key=lambda o: o["center"])

        boxes = []
        cursor = a
        for op in ops:
            lo, hi = op["center"] - op["width"] / 2, op["center"] + op["width"] / 2
            if lo > cursor + 1e-4:
                boxes.append((cursor, lo))
            # linteau et allege
            boxes.append((lo, hi, op["top"], z1))
            if op["bottom"] > z0 + 1e-4:
                boxes.append((lo, hi, z0, op["bottom"]))
            cursor = hi
        if b > cursor + 1e-4:
            boxes.append((cursor, b))

        geo = []
        for bx in boxes:
            s0, s1 = bx[0], bx[1]
            bz0, bz1 = (bx[2], bx[3]) if len(bx) == 4 else (z0, z1)
            if run["axis"] == "x":
                geo.append((s0, s1, run["line"] - half, run["line"] + half, bz0, bz1))
            else:
                geo.append((run["line"] - half, run["line"] + half, s0, s1, bz0, bz1))

        kind = "WallExt" if run["ext"] else "WallInt"
        key = "%s_%s_%s%g" % (prefix, kind, run["axis"], run["line"])
        counter[key] = counter.get(key, 0) + 1
        name = "%s_%d" % (key, counter[key])
        coll = ("EXTERIOR/Walls" if run["ext"] else "%s/Walls" % level.name)
        mat = level.ext_mat if run["ext"] else level.wall_mat
        make_mesh_object(name, geo, coll, mat, props={"hos_type": "wall_ext" if run["ext"] else "wall_int",
                                                      "hos_rooms": ", ".join(run["rooms"]), "hos_level": level.key})

    return openings


# =============================================================================
#  GEOMETRIE : DALLES, PLAFONDS, TREMIES
# =============================================================================

def subtract_rect(rect, hole):
    """Soustrait un rectangle 'hole' de 'rect' ; retourne jusqu'a 4 rectangles."""
    x0, x1, y0, y1 = rect
    hx0, hx1, hy0, hy1 = hole
    ix0, ix1, iy0, iy1 = max(x0, hx0), min(x1, hx1), max(y0, hy0), min(y1, hy1)
    if ix1 - ix0 <= 1e-6 or iy1 - iy0 <= 1e-6:
        return [rect]
    out = []
    if iy0 > y0 + 1e-6:
        out.append((x0, x1, y0, iy0))
    if iy1 < y1 - 1e-6:
        out.append((x0, x1, iy1, y1))
    if ix0 > x0 + 1e-6:
        out.append((x0, ix0, iy0, iy1))
    if ix1 < x1 - 1e-6:
        out.append((ix1, x1, iy0, iy1))
    return out


def subtract_rects_list(rects, hole):
    out = []
    for r in rects:
        out.extend(subtract_rect(r, hole))
    return out


def subtract_rects(rect, holes):
    rects = [rect]
    for h in holes:
        nxt = []
        for r in rects:
            nxt.extend(subtract_rect(r, h))
        rects = nxt
    return rects


def build_slabs(level, prefix):
    holes_above = [r.rect for lv in level.above for r in lv.rooms if not r.floor]
    for r in level.rooms:
        if r.floor:
            mat = r.floor_mat or level.floor_mat
            make_mesh_object("%s_Floor_%s" % (prefix, r.name), [(r.x0, r.x1, r.y0, r.y1, level.z - SLAB, level.z)],
                             "%s/Floors" % level.name, mat, props={"hos_type": "floor", "hos_room": r.name, "hos_level": level.key})
        if r.ceiling:
            rect = r.rect
            slopes = exterior_sides(level, r) if level.slope_run > 0 else []
            if slopes:
                rect = shrink_rect(rect, slopes, level.slope_run)
                build_sloped_ceilings(level, prefix, r, slopes)
            parts = subtract_rects(rect, holes_above) if rect else []
            boxes = [(p[0], p[1], p[2], p[3], level.ceiling_z, level.ceiling_z + SLAB) for p in parts]
            if boxes:
                make_mesh_object("%s_Ceiling_%s" % (prefix, r.name), boxes, "%s/Ceilings" % level.name, level.ceil_mat,
                                 props={"hos_type": "ceiling", "hos_room": r.name, "hos_level": level.key})


def exterior_sides(level, room):
    """Cotes de la piece qui donnent sur l'exterieur (aucune autre piece de l'autre cote)."""
    sides = []
    for side in ("S", "N", "W", "E"):
        axis, line, a, b = room.edge(side)
        neighbours = [o for o in level.rooms if o is not room and
                      abs((o.edge({"S": "N", "N": "S", "W": "E", "E": "W"}[side])[1]) - line) < 1e-6 and
                      rect_overlap_1d(a, b, *o.edge({"S": "N", "N": "S", "W": "E", "E": "W"}[side])[2:])]
        if not neighbours:
            sides.append(side)
    return sides


def shrink_rect(rect, sides, d):
    x0, x1, y0, y1 = rect
    if "W" in sides: x0 += d
    if "E" in sides: x1 -= d
    if "S" in sides: y0 += d
    if "N" in sides: y1 -= d
    if x1 - x0 <= 1e-6 or y1 - y0 <= 1e-6:
        return None
    return (x0, x1, y0, y1)


def build_sloped_ceilings(level, prefix, room, sides):
    """Rampants : du sommet du mur exterieur (ext_top) jusqu'au plafond plat, sur slope_run metres."""
    z_low = level.ext_top if level.ext_top is not None else level.ceiling_z
    z_high = level.ceiling_z
    run = level.slope_run
    x0, x1, y0, y1 = room.rect
    for side in sides:
        if side == "W":
            quad = [(x0, y0, z_low), (x0 + run, y0, z_high), (x0 + run, y1, z_high), (x0, y1, z_low)]
        elif side == "E":
            quad = [(x1 - run, y0, z_high), (x1, y0, z_low), (x1, y1, z_low), (x1 - run, y1, z_high)]
        elif side == "S":
            quad = [(x0, y0, z_low), (x1, y0, z_low), (x1, y0 + run, z_high), (x0, y0 + run, z_high)]
        else:
            quad = [(x0, y1 - run, z_high), (x1, y1 - run, z_high), (x1, y1, z_low), (x0, y1, z_low)]
        verts = quad + [(x, y, z + SLAB) for (x, y, z) in quad]
        faces = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
        make_poly_object("%s_CeilingSlope_%s_%s" % (prefix, room.name, side), verts, faces, "%s/Ceilings" % level.name,
                         level.ceil_mat, props={"hos_type": "ceiling", "hos_room": room.name, "hos_level": level.key})


# =============================================================================
#  GEOMETRIE : ESCALIERS (vraies marches)
# =============================================================================

def step_count(rise, max_rise=STEP_RISE_MAX):
    return max(1, int(math.ceil(rise / max_rise - 1e-6)))


def flight_boxes(lane, y_start, y_end, z_start, z_end, going_north=True, thickness=0.25):
    """Marches d'une volee le long de Y sur la 'lane' (x0, x1)."""
    n = step_count(abs(z_end - z_start))
    riser = (z_end - z_start) / n
    length = abs(y_end - y_start)
    tread = length / n
    boxes = []
    for i in range(n):
        top = z_start + (i + 1) * riser
        if going_north:
            ya, yb = y_start + i * tread, y_start + (i + 1) * tread
        else:
            ya, yb = y_start - (i + 1) * tread, y_start - i * tread
        boxes.append((lane[0], lane[1], ya, yb, top - riser - thickness, top))
    return boxes, n, riser, tread


def build_stair_U(level_bottom, room_name, z_levels, coll_prefix, mat, name):
    """Escalier a deux volees paralleles (entree et arrivee au sud, palier au nord).
    z_levels : hauteurs de sol successives desservies (bas -> haut)."""
    r = level_bottom.room(room_name)
    W = r.x1 - r.x0
    lane_w = (W - 0.1) / 2
    lane1 = (r.x0, r.x0 + lane_w)
    lane2 = (r.x1 - lane_w, r.x1)
    y_flight0 = r.y0 + ARRIVAL
    y_flight1 = r.y1 - LANDING
    boxes = []
    report = []
    for i, (za, zb) in enumerate(zip(z_levels[:-1], z_levels[1:])):
        z_mid = (za + zb) / 2
        f1, n1, riser, tread = flight_boxes(lane1, y_flight0, y_flight1, za, z_mid, True)
        f2, n2, _, _ = flight_boxes(lane2, y_flight1, y_flight0, z_mid, zb, False)
        boxes.extend(f1)
        boxes.extend(f2)
        boxes.append((r.x0, r.x1, y_flight1, r.y1, z_mid - 0.3, z_mid))          # palier intermediaire
        boxes.append((r.x0, r.x1, r.y0, y_flight0, zb - SLAB, zb))               # bande d'arrivee du niveau haut
        report.append("%s: %d+%d marches, contremarche %.3f, giron %.3f" % (name, n1, n2, riser, tread))
    make_mesh_object(name, boxes, "%s/Stairs" % coll_prefix, mat, props={"hos_type": "stairs", "hos_room": room_name})
    return report


def build_stair_straight(rect, z0, z1, coll_prefix, mat, name, direction="N", start_offset=0.0, thickness=0.25):
    """Volee droite dans 'rect', montant vers 'direction' (N/S/E/W)."""
    x0, x1, y0, y1 = rect
    boxes = []
    if direction in ("N", "S"):
        lane = (x0, x1)
        if direction == "N":
            f, n, riser, tread = flight_boxes(lane, y0 + start_offset, y1, z0, z1, True, thickness)
        else:
            f, n, riser, tread = flight_boxes(lane, y1 - start_offset, y0, z0, z1, False, thickness)
        boxes.extend(f)
    else:
        # meme chose le long de X : on construit en Y puis on permute les axes
        lane = (y0, y1)
        if direction == "E":
            f, n, riser, tread = flight_boxes(lane, x0 + start_offset, x1, z0, z1, True, thickness)
        else:
            f, n, riser, tread = flight_boxes(lane, x1 - start_offset, x0, z0, z1, False, thickness)
        boxes.extend([(b[2], b[3], b[0], b[1], b[4], b[5]) for b in f])
    make_mesh_object(name, boxes, "%s/Stairs" % coll_prefix, mat, props={"hos_type": "stairs"})
    return "%s: %d marches, contremarche %.3f, giron %.3f" % (name, n, riser, tread)


def build_stair_imperial(level, z_top, coll_prefix, mat, name, x0=22, x1=30, y0=13, y1=19, lane=2.0, landing=2.5):
    """Escalier imperial : volee centrale vers le nord, palier, deux volees retour vers le sud."""
    z0 = level.z
    z_mid = (z0 + z_top) / 2
    y_land = y1 - landing
    boxes = []
    center = (x0 + lane, x1 - lane)
    f, n1, riser, tread = flight_boxes(center, y0, y_land, z0, z_mid, True, 0.3)
    boxes.extend(f)
    boxes.append((x0, x1, y_land, y1, z_mid - 0.2, z_mid))   # 0.2 d'epaisseur : 1.9 m libres dans le placard dessous
    fw, n2, _, _ = flight_boxes((x0, x0 + lane), y_land, y0, z_mid, z_top, False, 0.3)
    fe, _, _, _ = flight_boxes((x1 - lane, x1), y_land, y0, z_mid, z_top, False, 0.3)
    boxes.extend(fw)
    boxes.extend(fe)
    # murs-bahuts (garde-corps pleins) le long de la volee centrale, pour le blockout
    make_mesh_object(name, boxes, "%s/Stairs" % coll_prefix, mat, props={"hos_type": "stairs", "hos_room": "Hall"})
    return "%s: %d + %d marches, contremarche %.3f, giron %.3f" % (name, n1, n2, riser, tread)


# =============================================================================
#  PORTES, FENETRES, GAMEPLAY
# =============================================================================

CIRCULATION_ROOMS = {"GrandCouloir", "CouloirService", "LongCouloir", "CouloirServiceF1", "CouloirEtroit", "CouloirNord",
                     "CouloirSud", "CouloirCellules", "Hall", "BalconS", "BalconW", "BalconE", "Pont", "GalerieW", "GalerieE",
                     "Tunnel1", "TunnelCoude", "Tunnel3", "AbriSortie"}


def door_placement(level, op, w):
    """Choisit le cote des gonds et le sens d'ouverture d'une porte.

    - gonds a l'extremite la plus proche d'un bout de mur (la porte se rabat contre le mur) ;
    - ouverture vers la piece desservie plutot que vers la circulation (couloir, hall, tunnel),
      et vers l'interieur pour les portes exterieures.
    Retourne (origine monde, lacet, +1/-1 = signe de la rotation Z qui ouvre, piece cible).
    """
    lo, hi = op["center"] - w / 2, op["center"] + w / 2
    run_a, run_b = op.get("run_a", lo), op.get("run_b", hi)
    hinge_low = (lo - run_a) <= (run_b - hi)
    u_hinge, u_free = (lo, hi) if hinge_low else (hi, lo)
    if op["axis"] == "x":
        origin = (u_hinge, op["line"], level.z)
        d = (1.0, 0.0) if hinge_low else (-1.0, 0.0)
    else:
        origin = (op["line"], u_hinge, level.z)
        d = (0.0, 1.0) if hinge_low else (0.0, -1.0)
    yaw = math.atan2(d[1], d[0])
    # piece vers laquelle la porte s'ouvre
    a, b = op["a"], op["b"]
    if b.startswith("EXT:"):
        target = a
    elif a in CIRCULATION_ROOMS and b not in CIRCULATION_ROOMS:
        target = b
    elif b in CIRCULATION_ROOMS and a not in CIRCULATION_ROOMS:
        target = a
    else:
        target = b
    cx, cy = level.room(target).center
    if op["axis"] == "x":
        o = (0.0, 1.0 if cy > op["line"] else -1.0)
    else:
        o = (1.0 if cx > op["line"] else -1.0, 0.0)
    local_y = (-math.sin(yaw), math.cos(yaw))
    swing = 1 if (local_y[0] * o[0] + local_y[1] * o[1]) > 0 else -1
    return origin, yaw, swing, target


def build_doors(level, prefix, openings):
    count = {}
    for op in openings:
        if op["kind"] == "window":
            continue
        w, h, has_leaf = DOOR_KINDS[op["kind"]]
        key = "%s_Door_%s_%s" % (prefix, op["a"], op["b"].replace("EXT:", "Ext"))
        count[key] = count.get(key, 0) + 1
        name = key if count[key] == 1 else "%s_%d" % (key, count[key])
        origin, yaw, swing, target = door_placement(level, op, w)
        if op["axis"] == "x":
            pt = (op["center"], op["line"], level.z + h / 2)
        else:
            pt = (op["line"], op["center"], level.z + h / 2)
        props = {"hos_door": op["kind"], "hos_rooms": "%s | %s" % (op["a"], op["b"]), "hos_level": level.key,
                 "hos_swing": swing, "hos_opens_into": target, "hos_open_angle": 95 if op["kind"] != "double" else 90,
                 "hos_width": w, "hos_height": h}
        if has_leaf:
            make_mesh_object(name, door_leaf_boxes(op["kind"], w, h), "%s/Doors" % level.name, door_leaf_material(op["kind"]),
                             props=dict(props, hos_type="door"), origin=origin, rotation_z=yaw)
        make_empty("GP_Door_%s" % name[len(prefix) + 6:], "GAMEPLAY_OBJECTS/Door_Points", pt, props=props, size=0.3)


def door_leaf_material(kind):
    return {"bars": "M_Metal_Rust", "metal": "M_Metal_Rust", "breakable": "M_Stone_Old"}.get(kind, "M_Wood_Dark")


def door_leaf_boxes(kind, w, h):
    """Geometrie locale d'un battant : charniere en x = 0, battant le long de +X, epaisseur selon Y."""
    if kind == "bars":
        boxes = [(0.0, w, -0.02, 0.02, 0.0, 0.08), (0.0, w, -0.02, 0.02, h - 0.08, h),
                 (0.0, 0.06, -0.02, 0.02, 0.0, h), (w - 0.06, w, -0.02, 0.02, 0.0, h),
                 (0.06, w - 0.06, -0.015, 0.015, h * 0.5 - 0.02, h * 0.5 + 0.02)]
        n = int(w / 0.12)
        for i in range(1, n):
            x = i * w / n
            boxes.append((x - 0.012, x + 0.012, -0.012, 0.012, 0.08, h - 0.08))
        return boxes
    if kind == "breakable":
        return [(0.0, w, -0.075, 0.075, 0.0, h)]
    if kind == "secret":
        # rayonnage / casier : battant epais avec des etageres en relief
        boxes = [(0.0, w, -0.16, 0.16, 0.0, h)]
        for k in range(1, 5):
            z = h * k / 5
            boxes.append((0.02, w - 0.02, 0.16, 0.19, z - 0.015, z + 0.015))
        return boxes
    if kind == "closet":
        return [(0.0, w, -0.02, 0.02, 0.0, h), (w - 0.08, w - 0.05, 0.02, 0.06, 1.0, 1.05)]
    thk = 0.06 if kind in ("entrance", "double", "metal") else 0.045
    boxes = [(0.0, w, -thk / 2, thk / 2, 0.0, h)]
    # panneaux en relief sur les deux faces
    leaves = 2 if kind in ("double", "entrance") else 1
    lw = w / leaves
    for i in range(leaves):
        x0 = i * lw
        for (za, zb) in ((0.12, h * 0.42), (h * 0.5, h - 0.12)):
            boxes.append((x0 + 0.08, x0 + lw - 0.08, thk / 2, thk / 2 + 0.012, za, zb))
            boxes.append((x0 + 0.08, x0 + lw - 0.08, -thk / 2 - 0.012, -thk / 2, za, zb))
        # poignee
        hx = x0 + lw - 0.12 if leaves == 1 else (x0 + lw - 0.1 if i == 0 else x0 + 0.1)
        boxes.append((hx - 0.03, hx + 0.03, thk / 2, thk / 2 + 0.06, 1.0, 1.06))
        boxes.append((hx - 0.03, hx + 0.03, -thk / 2 - 0.06, -thk / 2, 1.0, 1.06))
    return boxes


def build_glass(level, prefix, openings):
    n = 0
    for op in openings:
        if op["kind"] != "window" or op.get("skip"):
            continue
        n += 1
        w = op["width"]
        if op["axis"] == "x":
            box = (op["center"] - w / 2, op["center"] + w / 2, op["line"] - 0.01, op["line"] + 0.01, op["bottom"], op["top"])
        else:
            box = (op["line"] - 0.01, op["line"] + 0.01, op["center"] - w / 2, op["center"] + w / 2, op["bottom"], op["top"])
        make_mesh_object("%s_Window_%s_%s_%d" % (prefix, op["a"], op["side"], n), [box], "EXTERIOR/Windows", "M_Glass_Dirty",
                         props={"hos_type": "window", "hos_level": level.key})


def build_room_markers(level, prefix):
    for r in level.rooms:
        if not r.floor:
            continue
        cx, cy = r.center
        floor_index = {"TUN": -2, "B1": -1, "SHED": -1, "GF": 0, "F1": 1, "F2": 2}.get(level.key, 0)
        make_empty("GP_Nav_%s_%s" % (prefix, r.name), "GAMEPLAY_OBJECTS/Navigation_Markers", (cx, cy, level.z + 0.1),
                   props={"hos_room": r.name, "hos_level": level.key, "hos_floor_index": floor_index,
                          "hos_w": r.x1 - r.x0, "hos_d": r.y1 - r.y0, "hos_h": level.height,
                          "hos_zone": r.zone or ""}, size=0.4)


# =============================================================================
#  VALIDATION
# =============================================================================

def validate_level(level, footprint):
    """Verifie que les pieces pavent l'emprise sans se chevaucher (echantillonnage 0.5 m)."""
    fx0, fx1, fy0, fy1 = footprint
    gaps, overlaps = 0, 0
    steps_x = int((fx1 - fx0) / 0.5)
    steps_y = int((fy1 - fy0) / 0.5)
    for i in range(steps_x):
        for j in range(steps_y):
            px, py = fx0 + (i + 0.5) * 0.5, fy0 + (j + 0.5) * 0.5
            hits = [r for r in level.rooms if r.x0 <= px <= r.x1 and r.y0 <= py <= r.y1]
            if not hits:
                gaps += 1
            elif len(hits) > 1:
                overlaps += 1
    return gaps, overlaps


# =============================================================================
#  TABLES : LE MANOIR
# =============================================================================

def R(*args, **kw):
    return Room(*args, **kw)


def define_levels():
    B1 = Level("B1", "BASEMENT", -3.2, 2.8, 0.5, 0.3, "M_Stone_Old", "M_Stone_Old", "M_Concrete_Wet", "M_Concrete_Wet", "M_Concrete_Wet")
    GF = Level("GF", "GROUND_FLOOR", 0.0, 3.8, 0.6, 0.25, "M_Plaster_Old", "M_Stone_Old", "M_WoodFloor", "M_Plaster_Old", "M_Wood_Dark")
    F1 = Level("F1", "FIRST_FLOOR", 4.2, 3.2, 0.6, 0.2, "M_Wallpaper_Old", "M_Stone_Old", "M_WoodFloor", "M_Plaster_Old", "M_Wood_Dark")
    F2 = Level("F2", "SECOND_FLOOR", 7.8, 2.8, 0.6, 0.2, "M_Plaster_Old", "M_Stone_Old", "M_Wood_Worn", "M_Wood_Worn", "M_Wood_Worn", top=True)
    F2.ext_top = F2.z + 1.4        # jambage des combles : le toit part de 9.2
    F2.slope_run = 2.0             # le rampant rejoint le plafond plat 2 m plus loin
    # Tunnel : plus profond que le sous-sol pour rester sous le terrain (-1.4) une fois hors de l'emprise,
    # et assez bas pour passer debout sous la base des murs exterieurs du sous-sol (-3.6).
    TUN = Level("TUN", "BASEMENT", -5.8, 2.4, 0.5, 0.3, "M_Stone_Old", "M_Stone_Old", "M_Stone_Old", "M_Stone_Old", "M_Concrete_Wet")
    # Abri de sortie au niveau du terrain ; ses murs descendent jusqu'au plafond du tunnel pour fermer la cage.
    SHED = Level("SHED", "BASEMENT", GROUND_Z, 2.4, 0.4, 0.2, "M_Stone_Old", "M_Stone_Old", "M_Concrete_Wet", "M_Concrete_Wet", "M_Concrete_Wet", top=True)
    SHED.wall_bottom = TUN.z + TUN.height

    # ------------------------------------------------------------------ REZ
    GF.rooms = [
        R("Salon", 0, 14, 0, 10),
        R("Musique", 14, 20, 0, 6),
        R("SdB_RDC", 14, 20, 6, 10, floor_mat="M_Tile_Old"),
        R("PuitsSecret", 0, 2, 10, 14, zone="secret", floor=False),
        R("PassageSecret", 0, 2, 14, 19, zone="secret", floor_mat="M_Stone_Old"),
        R("Bibliotheque", 2, 12, 10, 19),
        R("Bureau", 12, 20, 10, 19),
        R("Hall", 20, 32, 0, 19, zone="hall", floor_mat="M_Tile_Old"),
        R("Reception", 32, 52, 0, 10),
        R("SalleAManger", 32, 44, 10, 19),
        R("Office", 44, 52, 10, 19, floor_mat="M_Tile_Old"),
        R("GrandCouloir", 0, 52, 19, 22, floor_mat="M_Tile_Old"),
        R("CouloirService", 0, 34, 22, 24, floor_mat="M_Tile_Old"),
        R("Cellier", 0, 8, 24, 30, floor_mat="M_Tile_Old"),
        R("EscService", 8, 12, 24, 30, floor=False, ceiling=False),
        R("Buanderie", 12, 18, 24, 30, floor_mat="M_Tile_Old"),
        R("SalleDomestiques", 18, 30, 24, 30),
        R("EscSousSol", 30, 34, 24, 30, floor=False),
        R("Cuisine", 34, 46, 22, 30, floor_mat="M_Tile_Old"),
        R("EntreeSecondaire", 46, 52, 22, 30, floor_mat="M_Tile_Old"),
    ]
    GF.doors = [
        ("Hall", "EXT:S", 0.5, "entrance"),
        ("Hall", "Musique", 0.5, "single"),
        ("Hall", "Bureau", 0.5, "single"),
        ("Hall", "Reception", 0.5, "double"),
        ("Hall", "GrandCouloir", 1.0 / 12, "opening"),
        ("Hall", "GrandCouloir", 11.0 / 12, "opening"),
        ("Salon", "Musique", 0.5, "single"),
        ("Musique", "SdB_RDC", 0.5, "single"),
        ("Salon", "Bibliotheque", 0.25, "double"),
        ("Bibliotheque", "PassageSecret", 0.6, "secret"),
        ("Bibliotheque", "Bureau", 0.2, "single"),
        ("Bibliotheque", "GrandCouloir", 0.5, "double"),
        ("Bureau", "GrandCouloir", 0.5, "single"),
        ("Reception", "SalleAManger", 0.5, "double"),
        ("Reception", "EXT:E", 0.5, "double"),
        ("SalleAManger", "GrandCouloir", 0.5, "double"),
        ("SalleAManger", "Office", 0.5, "service"),
        ("Office", "GrandCouloir", 0.5, "service"),
        ("Cuisine", "GrandCouloir", 0.5, "service"),
        ("Cuisine", "CouloirService", 0.5, "service"),
        ("Cuisine", "EntreeSecondaire", 0.5, "service"),
        ("EntreeSecondaire", "GrandCouloir", 0.5, "single"),
        ("EntreeSecondaire", "EXT:N", 0.3, "single"),
        ("GrandCouloir", "CouloirService", 0.08, "opening"),
        ("GrandCouloir", "CouloirService", 0.92, "opening"),
        ("Cellier", "CouloirService", 0.5, "service"),
        ("EscService", "CouloirService", 0.5, "service"),
        ("Buanderie", "CouloirService", 0.5, "service"),
        ("SalleDomestiques", "CouloirService", 0.5, "service"),
        ("EscSousSol", "CouloirService", 0.5, "service"),
    ]
    GF.windows = [
        ("Salon", "S", [0.2, 0.5, 0.8]), ("Salon", "W", [0.3, 0.7]), ("Musique", "S", [0.5]),
        ("Hall", "S", [0.2, 0.8]),
        ("Reception", "S", [0.1, 0.3, 0.5, 0.7, 0.9]), ("Reception", "E", [0.2, 0.8]),
        ("Office", "E", [0.3, 0.7]), ("Cuisine", "N", [0.2, 0.5, 0.8]),
        ("EntreeSecondaire", "N", [0.75]), ("EntreeSecondaire", "E", [0.5]),
        ("Cellier", "N", [0.5]), ("Buanderie", "N", [0.5]), ("SalleDomestiques", "N", [0.3, 0.7]),
        ("GrandCouloir", "W", [0.5]), ("GrandCouloir", "E", [0.5]), ("CouloirService", "W", [0.5]),
    ]

    # ------------------------------------------------------------------ 1ER
    F1.rooms = [
        R("Chambre2", 0, 12, 0, 10),
        R("Chambre3", 12, 20, 0, 10),
        R("BalconS", 20, 32, 0, 4.5, zone="hall", floor_mat="M_Tile_Old"),
        R("BalconW", 20, 22.5, 4.5, 11, zone="hall", floor_mat="M_Tile_Old"),
        R("VideHall", 22.5, 29.5, 4.5, 11, zone="hall", floor=False),
        R("BalconE", 29.5, 32, 4.5, 11, zone="hall", floor_mat="M_Tile_Old"),
        R("ChambrePrincipale", 32, 44, 0, 10),
        R("SdBPrincipale", 44, 52, 0, 10, floor_mat="M_Tile_Old"),
        R("ChambreEnfant", 0, 12, 10, 19),
        R("BureauPrive", 12, 20, 10, 19),
        R("Pont", 20, 32, 11, 13, zone="hall", floor_mat="M_Tile_Old"),
        R("GalerieW", 20, 22, 13, 19, zone="hall", floor_mat="M_Tile_Old"),
        R("PalierGrandEsc", 22, 30, 13, 19, zone="hall", floor=False),
        R("GalerieE", 30, 32, 13, 19, zone="hall", floor_mat="M_Tile_Old"),
        R("SdBSecondaire", 32, 38, 10, 19, floor_mat="M_Tile_Old"),
        R("Dressing", 38, 44, 10, 19),
        R("Lingerie", 44, 52, 10, 19),
        R("LongCouloir", 0, 52, 19, 22),
        R("CouloirServiceF1", 0, 34, 22, 24),
        R("ChambreDomestique1", 0, 8, 24, 30),
        R("EscService", 8, 12, 24, 30, floor=False, ceiling=False),
        R("ChambreDomestique2", 12, 18, 24, 30),
        R("ChambreDomestique3", 18, 24, 24, 30),
        R("Debarras", 24, 30, 24, 30),
        R("Placard", 30, 34, 24, 30),
        R("ChambreAmis", 34, 46, 22, 30),
        R("Loggia", 46, 52, 22, 30),
    ]
    F1.doors = [
        ("Chambre3", "BalconS", 0.5, "single"),
        ("Chambre2", "Chambre3", 0.25, "single"),
        ("Chambre2", "ChambreEnfant", 0.25, "single"),
        ("Chambre3", "BureauPrive", 0.5, "single"),
        ("BureauPrive", "ChambreEnfant", 0.3, "secret"),
        ("ChambreEnfant", "LongCouloir", 0.5, "single"),
        ("BureauPrive", "LongCouloir", 0.5, "single"),
        ("GalerieW", "LongCouloir", 0.5, "opening"),
        ("GalerieE", "LongCouloir", 0.5, "opening"),
        ("BalconE", "ChambrePrincipale", 0.5, "double"),
        ("ChambrePrincipale", "Dressing", 0.5, "single"),
        ("ChambrePrincipale", "SdBPrincipale", 0.5, "single"),
        ("Dressing", "LongCouloir", 0.5, "single"),
        ("SdBSecondaire", "LongCouloir", 0.5, "single"),
        ("Lingerie", "LongCouloir", 0.5, "single"),
        ("ChambreAmis", "LongCouloir", 0.5, "single"),
        ("Loggia", "LongCouloir", 0.5, "single"),
        ("ChambreAmis", "Loggia", 0.5, "single"),
        ("LongCouloir", "CouloirServiceF1", 0.08, "opening"),
        ("LongCouloir", "CouloirServiceF1", 0.92, "opening"),
        ("ChambreDomestique1", "CouloirServiceF1", 0.5, "service"),
        ("EscService", "CouloirServiceF1", 0.5, "service"),
        ("ChambreDomestique2", "CouloirServiceF1", 0.5, "service"),
        ("ChambreDomestique3", "CouloirServiceF1", 0.5, "service"),
        ("Debarras", "CouloirServiceF1", 0.5, "service"),
        ("Placard", "CouloirServiceF1", 0.5, "service"),
    ]
    F1.windows = [
        ("Chambre2", "S", [0.3, 0.7]), ("Chambre2", "W", [0.5]), ("Chambre3", "S", [0.5]),
        ("ChambreEnfant", "W", [0.3, 0.7]), ("BalconS", "S", [0.2, 0.5, 0.8]),
        ("ChambrePrincipale", "S", [0.2, 0.5, 0.8]), ("SdBPrincipale", "S", [0.5]), ("SdBPrincipale", "E", [0.5]),
        ("Lingerie", "E", [0.5]), ("LongCouloir", "W", [0.5]), ("LongCouloir", "E", [0.5]),
        ("ChambreDomestique1", "N", [0.5]), ("ChambreDomestique2", "N", [0.5]), ("ChambreDomestique3", "N", [0.5]),
        ("Debarras", "N", [0.5]), ("ChambreAmis", "N", [0.15, 0.85]), ("Loggia", "N", [0.5]), ("Loggia", "E", [0.5]),
        ("CouloirServiceF1", "W", [0.5]),
    ]

    # ------------------------------------------------------------------ 2E (combles amenages, abandonnes)
    F2.rooms = [
        R("CombleOuest", 0, 4, 0, 30, zone="comble"),
        R("CombleEst", 48, 52, 0, 30, zone="comble"),
        R("CombleSud", 4, 48, 0, 4, zone="comble"),
        R("CombleSO", 4, 22, 4, 8),
        R("SalleDeJeux", 22, 30, 4, 19.4),
        R("CombleSE", 30, 40, 4, 8),
        R("Reduit", 40, 44, 4, 8),
        R("PieceSecrete", 44, 48, 4, 8),
        R("ChambreAbandonnee1", 4, 14, 8, 19.4),
        R("ChambreAbandonnee2", 14, 22, 8, 19.4),
        R("ChambreAbandonnee3", 30, 40, 8, 19.4),
        R("Stockage", 40, 48, 8, 19.4),
        R("CouloirEtroit", 4, 48, 19.4, 21),
        R("Grenier", 4, 40, 21, 24, zone="grenier"),
        R("EscGrenier", 40, 44, 21, 24),
        R("DebarrasF2", 44, 48, 21, 24),
        R("GrenierOuest", 4, 8, 24, 30, zone="grenier"),
        R("EscService", 8, 12, 24, 30, floor=False),
        R("GrenierNord", 12, 40, 24, 30, zone="grenier"),
        R("StockageNord", 40, 48, 24, 30),
    ]
    F2.doors = [
        ("ChambreAbandonnee1", "CouloirEtroit", 0.5, "single"),
        ("ChambreAbandonnee2", "CouloirEtroit", 0.5, "single"),
        ("SalleDeJeux", "CouloirEtroit", 0.5, "double"),
        ("ChambreAbandonnee3", "CouloirEtroit", 0.5, "single"),
        ("Stockage", "CouloirEtroit", 0.5, "single"),
        ("Grenier", "CouloirEtroit", 0.15, "opening"),
        ("Grenier", "CouloirEtroit", 0.85, "opening"),
        ("EscGrenier", "CouloirEtroit", 0.5, "service"),
        ("DebarrasF2", "CouloirEtroit", 0.5, "service"),
        ("EscService", "Grenier", 0.5, "service"),
        ("GrenierNord", "StockageNord", 0.5, "service"),
        ("Stockage", "PieceSecrete", 0.5, "secret"),
        ("Stockage", "Reduit", 0.5, "hatch"),
        ("CombleSO", "ChambreAbandonnee2", 0.5, "hatch"),
        ("CombleSE", "ChambreAbandonnee3", 0.5, "hatch"),
        ("CombleSud", "CombleSO", 0.5, "hatch"),
        ("CombleOuest", "Grenier", 0.5, "hatch"),
        ("CombleEst", "Stockage", 0.5, "hatch"),
    ]
    F2.windows = [
        ("CombleOuest", "W", [0.25, 0.75]), ("CombleEst", "E", [0.25, 0.75]), ("CombleSud", "S", [0.15, 0.38, 0.62, 0.85]),
        ("GrenierNord", "N", [0.2, 0.5, 0.8]), ("GrenierOuest", "N", [0.5]), ("StockageNord", "N", [0.5]),
    ]

    # ------------------------------------------------------------------ SOUS-SOL
    B1.rooms = [
        R("SalleRituelle", 0, 12, 0, 14, floor_mat="M_Stone_Old"),
        R("CaveAVin", 0, 12, 14, 21),
        R("CouloirSud", 12, 52, 0, 2),
        R("Atelier", 12, 24, 2, 21),
        R("Remise", 24, 28, 2, 12),
        R("PieceCachee", 24, 28, 12, 21),
        R("Cellule1", 28, 33, 2, 8), R("Cellule2", 28, 33, 8, 14), R("Cellule3", 28, 33, 14, 21),
        R("CouloirCellules", 33, 35, 2, 21),
        R("Cellule4", 35, 40, 2, 8), R("Cellule5", 35, 40, 8, 14), R("Cellule6", 35, 40, 14, 21),
        R("Archives", 40, 52, 2, 21),
        R("CouloirNord", 0, 52, 21, 24),
        R("Chaufferie", 0, 8, 24, 30),
        R("EscService", 8, 12, 24, 30, ceiling=False),
        R("LocalTechnique", 12, 26, 24, 30),
        R("Reserve", 26, 30, 24, 30),
        R("EscSousSol", 30, 34, 24, 30, ceiling=False),
        R("Stockage", 34, 42, 24, 30),
        R("Morgue", 42, 48, 24, 30, zone="morgue", floor_mat="M_Tile_Old"),
        R("MorgueN", 48, 52, 28, 30, zone="morgue", floor_mat="M_Tile_Old"),
        R("MorgueS", 48, 52, 24, 26, zone="morgue", floor_mat="M_Tile_Old"),
        R("TunnelEsc", 48, 52, 26, 28, floor=False),          # descente vers le tunnel (-5.8)
    ]
    B1.doors = [
        ("CouloirNord", "Chaufferie", 0.5, "metal"),
        ("CouloirNord", "EscService", 0.5, "service"),
        ("CouloirNord", "LocalTechnique", 0.5, "metal"),
        ("CouloirNord", "Reserve", 0.5, "service"),
        ("CouloirNord", "EscSousSol", 0.5, "service"),
        ("CouloirNord", "Stockage", 0.5, "service"),
        ("CouloirNord", "Morgue", 0.5, "metal"),
        ("CouloirNord", "CaveAVin", 0.5, "service"),
        ("CouloirNord", "Atelier", 0.5, "service"),
        ("CouloirNord", "CouloirCellules", 0.5, "bars"),
        ("CouloirNord", "Archives", 0.5, "service"),
        ("CouloirSud", "Atelier", 0.5, "service"),
        ("CouloirSud", "Remise", 0.5, "service"),
        ("CouloirSud", "CouloirCellules", 0.5, "bars"),
        ("CouloirSud", "Archives", 0.5, "service"),
        ("Atelier", "Remise", 0.5, "service"),
        ("Atelier", "PieceCachee", 0.5, "breakable"),
        ("CaveAVin", "SalleRituelle", 0.3, "secret"),
        ("Cellule1", "CouloirCellules", 0.5, "bars"), ("Cellule2", "CouloirCellules", 0.5, "bars"),
        ("Cellule3", "CouloirCellules", 0.5, "bars"), ("Cellule4", "CouloirCellules", 0.5, "bars"),
        ("Cellule5", "CouloirCellules", 0.5, "bars"), ("Cellule6", "CouloirCellules", 0.5, "bars"),
        ("Morgue", "TunnelEsc", 0.5, "metal"),
    ]
    B1.windows = [
        ("CaveAVin", "W", [0.5]), ("Chaufferie", "N", [0.5]), ("Chaufferie", "W", [0.5]),
        ("LocalTechnique", "N", [0.3, 0.7]), ("Stockage", "N", [0.5]), ("Morgue", "N", [0.5]), ("MorgueS", "E", [0.5]),
        ("Archives", "E", [0.3, 0.7]), ("CouloirSud", "S", [0.2, 0.5, 0.8]),
    ]

    # ------------------------------------------------------------------ TUNNEL (-5.8)
    TUN.rooms = [
        R("Tunnel1", 52, 74, 26, 28, zone="tunnel"),
        R("TunnelCoude", 74, 76, 26, 40, zone="tunnel"),
        R("Tunnel3", 76, 90, 38, 40, zone="tunnel"),
        R("EscSecondaire", 90, 94, 38, 44, zone="tunnel", ceiling=False),
    ]
    TUN.doors = [("Tunnel1", "EXT:W", 0.5, "opening")]   # arrivee de l'escalier de la morgue
    TUN.windows = []

    # ------------------------------------------------------------------ ABRI DE SORTIE DU TUNNEL (niveau du terrain)
    SHED.rooms = [
        R("AbriEscalier", 90, 94, 38, 44, zone="abri", floor=False),
        R("AbriSortie", 90, 94, 35, 38, zone="abri", floor_mat="M_Stone_Old"),   # contre la bande d'arrivee (y 38..39)
    ]
    SHED.doors = [("AbriSortie", "EXT:S", 0.5, "metal")]
    SHED.windows = []

    B1.above = [GF]
    GF.above = [F1]
    F1.above = [F2]
    TUN.above = [SHED]

    return [B1, GF, F1, F2, TUN, SHED]


# =============================================================================
#  EXTERIEUR : TOIT, PORCHE, PERRON, TERRASSE, TOURELLE, CHEMINEES, TERRAIN
# =============================================================================

# Souches de cheminee, a l'aplomb des foyers definis dans manor_details.py
CHIMNEYS = [(7, 10), (12, 5), (12, 14.5), (35, 10), (38, 30), (22, 11.7)]
BUILD_DETAILS = True
DETAILS_PATH = os.path.join(os.path.dirname(BLEND_PATH), "manor_details.py")
CIRCULATION_PATH = os.path.join(os.path.dirname(BLEND_PATH), "manor_circulation.py")

def build_hip_roof(x0, x1, y0, y1, z_eave, pitch_deg, overhang, name, mat):
    """Toit a quatre pentes, faitage le long de X, avec epaisseur (Solidify)."""
    x0, x1, y0, y1 = x0 - overhang, x1 + overhang, y0 - overhang, y1 + overhang
    half = (y1 - y0) / 2
    rise = math.tan(math.radians(pitch_deg)) * half
    yc = (y0 + y1) / 2
    verts = [(x0, y0, z_eave), (x1, y0, z_eave), (x1, y1, z_eave), (x0, y1, z_eave),
             (x0 + half, yc, z_eave + rise), (x1 - half, yc, z_eave + rise)]
    faces = [(0, 1, 5, 4), (2, 3, 4, 5), (1, 2, 5), (3, 0, 4)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    mesh.materials.append(get_material(mat))
    mod = obj.modifiers.new("Thickness", "SOLIDIFY")
    mod.thickness = 0.35
    mod.offset = 1.0
    get_collection("EXTERIOR/Roof").objects.link(obj)
    obj["hos_type"] = "roof"
    return obj, z_eave + rise


def build_pyramid(cx, cy, half, z0, z1, name, mat, coll="EXTERIOR/Roof"):
    verts = [(cx - half, cy - half, z0), (cx + half, cy - half, z0), (cx + half, cy + half, z0), (cx - half, cy + half, z0), (cx, cy, z1)]
    faces = [(0, 3, 2, 1), (0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    mesh.materials.append(get_material(mat))
    get_collection(coll).objects.link(obj)
    return obj


def build_cylinder(cx, cy, r, z0, z1, name, mat, coll, segments=16):
    verts, faces = [], []
    for k in range(segments):
        a = 2 * math.pi * k / segments
        verts.append((cx + r * math.cos(a), cy + r * math.sin(a), z0))
    for k in range(segments):
        a = 2 * math.pi * k / segments
        verts.append((cx + r * math.cos(a), cy + r * math.sin(a), z1))
    for k in range(segments):
        n = (k + 1) % segments
        faces.append((k, n, n + segments, k + segments))
    faces.append(tuple(reversed(range(segments))))
    faces.append(tuple(range(segments, 2 * segments)))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    mesh.materials.append(get_material(mat))
    get_collection(coll).objects.link(obj)
    return obj


def build_exterior(levels, reports):
    GF = levels[1]
    F2 = levels[3]
    z_top_walls = F2.ext_top if F2.ext_top is not None else F2.ceiling_z   # 9.2 : sommet des murs exterieurs

    # Toit principal (4 pentes a 35 degres) et pignons des ailes
    roof, ridge_z = build_hip_roof(0, 52, 0, 30, z_top_walls, 35, 0.8, "EXT_Roof_Main", "M_Roof_Tiles")
    reports.append("Toit : egout %.1f m, faitage %.1f m" % (z_top_walls, ridge_z))

    # Tourelle nord-est
    make_mesh_object("EXT_Tourelle_NE", [(47, 52, 25, 30, z_top_walls - 0.5, 15.5)], "EXTERIOR/Decorations", "M_Stone_Old",
                     props={"hos_type": "decor"})
    build_pyramid(49.5, 27.5, 3.0, 15.5, 20.0, "EXT_Tourelle_NE_Roof", "M_Roof_Tiles")

    # Cheminees : depassent la pente de 1.2 m a leur position
    slope = math.tan(math.radians(35))
    for i, (cx, cy) in enumerate(CHIMNEYS):
        roof_z = z_top_walls + slope * min(cx + 0.8, 52.8 - cx, cy + 0.8, 30.8 - cy)
        make_mesh_object("EXT_Chimney_%d" % (i + 1), [(cx - 0.6, cx + 0.6, cy - 0.6, cy + 0.6, z_top_walls, roof_z + 1.2)],
                         "EXTERIOR/Decorations", "M_Stone_Old", props={"hos_type": "decor"})

    # Porche : dalle, colonnes, fronton, perron
    make_mesh_object("EXT_Porch_Slab", [(21, 31, -4.5, 0, GF.ceiling_z, GF.ceiling_z + 0.4)], "EXTERIOR/Decorations", "M_Stone_Old",
                     props={"hos_type": "decor"})
    make_mesh_object("EXT_Porch_Pediment", [(21, 31, -4.5, -3.9, GF.ceiling_z + 0.4, GF.ceiling_z + 1.6)], "EXTERIOR/Decorations",
                     "M_Stone_Old", props={"hos_type": "decor"})
    make_mesh_object("EXT_Porch_Floor", [(21, 31, -4.5, 0, -SLAB, 0)], "EXTERIOR/Decorations", "M_Stone_Old", props={"hos_type": "decor"})
    for i, cx in enumerate([21.6, 24.4, 27.6, 30.4]):
        build_cylinder(cx, -3.9, 0.35, 0, GF.ceiling_z, "EXT_Porch_Column_%d" % (i + 1), "M_Stone_Old", "EXTERIOR/Decorations")
    reports.append(build_stair_straight((22, 30, -6.9, -4.5), GROUND_Z, 0.0, "EXTERIOR", "M_Stone_Old", "EXT_Perron", direction="N"))

    # Terrasse est (devant la salle de reception) et ses marches
    make_mesh_object("EXT_Terrace_E", [(52.3, 58, 0, 10, -SLAB, 0)], "EXTERIOR/Decorations", "M_Stone_Old", props={"hos_type": "decor"})
    reports.append(build_stair_straight((58, 60.4, 3, 7), GROUND_Z, 0.0, "EXTERIOR", "M_Stone_Old", "EXT_Terrace_Steps", direction="W"))

    # Marches de l'entree secondaire (nord)
    reports.append(build_stair_straight((47.5, 50.5, 30.3, 32.7), GROUND_Z, 0.0, "EXTERIOR", "M_Stone_Old", "EXT_BackSteps", direction="S"))

    # Toit plat de l'abri du tunnel
    make_mesh_object("EXT_Shed_Roof", [(89.6, 94.4, 34.6, 44.4, GROUND_Z + 2.4, GROUND_Z + 2.7)], "EXTERIOR/Roof", "M_Roof_Tiles",
                     props={"hos_type": "roof"})

    # Terrain de reference (ne pas exporter : sert a juger les hauteurs), troue sous la
    # maison et sous l'abri pour ne pas traverser le sous-sol.
    ground = [(-30, 120, -40, 70)]
    for hole in ((0, 52, 0, 30), (89.6, 94.4, 34.6, 44.4)):
        ground = subtract_rects_list(ground, hole)
    make_mesh_object("REF_Ground", [(g[0], g[1], g[2], g[3], GROUND_Z - 0.05, GROUND_Z) for g in ground], "COLLISION_GUIDES",
                     "M_Ground", props={"hos_type": "reference", "hos_export": False})


# =============================================================================
#  GAMEPLAY
# =============================================================================

def build_gameplay(levels):
    B1, GF, F1, F2, TUN, SHED = levels
    spawns = [("Spawn_Perron", (26, -8, GROUND_Z + 0.1)), ("Spawn_Hall", (26, 6, 0.1)), ("Spawn_Cuisine", (40, 26, 0.1)),
              ("Spawn_SousSol_Cellules", (34, 11, B1.z + 0.1)), ("Spawn_F2_Couloir", (26, 20.2, F2.z + 0.1))]
    for name, loc in spawns:
        make_empty("GP_" + name, "GAMEPLAY_OBJECTS/Spawn_Points", loc, props={"hos_spawn": True}, size=0.6)

    hides = [("Sous_GrandEscalier", (26, 18, 0.1)), ("Cellier", (2, 27, 0.1)), ("Placard_Musique", (19, 5, 0.1)),
             ("Placard_F1", (32, 27, F1.z + 0.1)), ("Dressing", (41, 14.5, F1.z + 0.1)), ("Armoire_Stockage_F2", (46, 10, F2.z + 0.1)),
             ("Reduit_F2", (42, 6, F2.z + 0.1)), ("Casier_CaveAVin", (2, 17, B1.z + 0.1)), ("Remise", (26, 4, B1.z + 0.1)),
             ("Tiroir_Morgue", (50, 27, B1.z + 0.1)), ("Comble_Ouest", (2, 15, F2.z + 0.1))]
    for name, loc in hides:
        make_empty("GP_Hide_" + name, "GAMEPLAY_OBJECTS/Hiding_Spots", loc, props={"hos_hide": True}, size=0.4)

    # Points de gameplay deja utilises par le prototype (fusible, symbole, sortie)
    make_empty("GP_Obj_FuseBox", "GAMEPLAY_OBJECTS/Navigation_Markers", (19, 29.7, B1.z + 1.5), props={"hos_objective": "fusebox"})
    make_empty("GP_Obj_Symbol", "GAMEPLAY_OBJECTS/Navigation_Markers", (6, 7, B1.z + 1.0), props={"hos_objective": "symbol"})
    make_empty("GP_Obj_TunnelExit", "GAMEPLAY_OBJECTS/Navigation_Markers", (92, 33.5, GROUND_Z + 0.1), props={"hos_objective": "exit"})
    make_empty("GP_Hide_Cellule5", "GAMEPLAY_OBJECTS/Hiding_Spots", (37.5, 11, B1.z + 0.1), props={"hos_hide": True}, size=0.4)


# =============================================================================
#  CONSTRUCTION
# =============================================================================

def build_manor(save=True):
    clear_previous()
    _collections.clear()
    get_collection("MANOR_ROOT")
    for sub in ("EXTERIOR/Walls", "EXTERIOR/Roof", "EXTERIOR/Windows", "EXTERIOR/Doors", "EXTERIOR/Decorations", "EXTERIOR/Stairs",
                "GROUND_FLOOR/Walls", "GROUND_FLOOR/Floors", "GROUND_FLOOR/Ceilings", "GROUND_FLOOR/Doors", "GROUND_FLOOR/Stairs",
                "FIRST_FLOOR/Walls", "FIRST_FLOOR/Floors", "FIRST_FLOOR/Ceilings", "FIRST_FLOOR/Doors", "FIRST_FLOOR/Stairs",
                "SECOND_FLOOR/Walls", "SECOND_FLOOR/Floors", "SECOND_FLOOR/Ceilings", "SECOND_FLOOR/Doors", "SECOND_FLOOR/Stairs",
                "BASEMENT/Walls", "BASEMENT/Floors", "BASEMENT/Ceilings", "BASEMENT/Doors", "BASEMENT/Tunnels", "BASEMENT/Stairs",
                "GAMEPLAY_OBJECTS/Door_Points", "GAMEPLAY_OBJECTS/Spawn_Points", "GAMEPLAY_OBJECTS/Hiding_Spots",
                "GAMEPLAY_OBJECTS/Navigation_Markers", "COLLISION_GUIDES", "GAMEPLAY_COLLISION", "VISUAL_MESH"):
        get_collection("MANOR_ROOT/" + sub)
    # Toutes les collections utilisees ensuite sont sous MANOR_ROOT
    for key in list(_collections.keys()):
        if key.startswith("MANOR_ROOT/"):
            _collections[key[len("MANOR_ROOT/"):]] = _collections[key]

    levels = define_levels()
    B1, GF, F1, F2, TUN, SHED = levels
    reports, stats = [], {}

    for lv in levels:
        prefix = lv.key
        openings = build_walls(lv, prefix)
        build_slabs(lv, prefix)
        build_doors(lv, prefix, openings)
        build_glass(lv, prefix, openings)
        build_room_markers(lv, prefix)
        gaps, overlaps = validate_level(lv, (0, 52, 0, 30)) if lv.key in ("B1", "GF", "F1", "F2") else (0, 0)
        stats[lv.key] = dict(rooms=len(lv.rooms), doors=len(lv.doors), gaps=gaps, overlaps=overlaps)

    # Escaliers
    reports.append(build_stair_imperial(GF, F1.z, "GROUND_FLOOR", "M_Wood_Dark", "GF_Stairs_GrandEscalier"))
    reports.extend(build_stair_U(B1, "EscService", [B1.z, GF.z, F1.z, F2.z], "GROUND_FLOOR", "M_Wood_Dark", "GF_Stairs_Service"))
    reports.extend(build_stair_U(B1, "EscSousSol", [B1.z, GF.z], "BASEMENT", "M_Concrete_Wet", "B1_Stairs_SousSol"))
    reports.append(build_stair_straight((0, 2, 10, 14), B1.z, GF.z, "BASEMENT", "M_Stone_Old", "B1_Stairs_PassageSecret", direction="N"))
    # Echelle du grenier : sur la bande nord de la piece (y 22.8..24), montant vers l'est ; la bande
    # sud reste libre devant la porte (sinon le linteau de la porte bloque la tete des la 2e marche).
    reports.append(build_stair_straight((40.8, 44, 22.8, 24), F2.z, F2.ceiling_z, "SECOND_FLOOR", "M_Wood_Worn", "F2_Stairs_Grenier", direction="E"))
    # La volee s'arrete a 0.6 m du mur exterieur : on passe dessous debout (1.8 m sous sa base a -3.6).
    reports.append(build_stair_straight((48, 51.4, 26, 28), TUN.z, B1.z, "BASEMENT", "M_Concrete_Wet", "B1_Stairs_Tunnel", direction="W"))
    reports.extend(build_stair_U(TUN, "EscSecondaire", [TUN.z, SHED.z], "BASEMENT", "M_Concrete_Wet", "TUN_Stairs_Secondaire"))
    # Parois de la tranchee de l'escalier de la morgue, sous le niveau du sous-sol
    make_mesh_object("B1_WallInt_TunnelEsc_Trench", [(47.9, 52.25, 25.7, 26.0, TUN.z - 2 * SLAB, B1.z),
                                                     (47.9, 52.25, 28.0, 28.3, TUN.z - 2 * SLAB, B1.z)],
                     "BASEMENT/Tunnels", "M_Stone_Old", props={"hos_type": "wall_int", "hos_level": "B1"})
    make_mesh_object("B1_Floor_TunnelEsc_Bas", [(51.3, 52.3, 26.0, 28.0, TUN.z - SLAB, TUN.z)], "BASEMENT/Tunnels", "M_Stone_Old",
                     props={"hos_type": "floor", "hos_level": "TUN", "hos_room": "TunnelEsc"})

    # Tunnels : les objets du tunnel vont dans BASEMENT/Tunnels
    tunnels = get_collection("BASEMENT/Tunnels")
    for obj in list(get_collection("BASEMENT/Floors").objects) + list(get_collection("BASEMENT/Ceilings").objects):
        if "Tunnel" in obj.name or "Abri" in obj.name:
            for c in obj.users_collection:
                c.objects.unlink(obj)
            tunnels.objects.link(obj)
    for obj in list(get_collection("EXTERIOR/Walls").objects) + list(get_collection("BASEMENT/Walls").objects):
        rooms = obj.get("hos_rooms", "")
        if "Tunnel" in rooms or "Abri" in rooms or "EscSecondaire" in rooms or obj.get("hos_level") in ("TUN", "SHED"):
            for c in obj.users_collection:
                c.objects.unlink(obj)
            tunnels.objects.link(obj)

    build_exterior(levels, reports)
    build_gameplay(levels)

    if BUILD_DETAILS and os.path.exists(DETAILS_PATH):
        details_globals = dict(globals())
        details_globals["__file__"] = DETAILS_PATH
        exec(compile(open(DETAILS_PATH, encoding="utf-8").read(), DETAILS_PATH, "exec"), details_globals)
        reports.extend(details_globals["build_details"](levels))

    if BUILD_DETAILS and os.path.exists(CIRCULATION_PATH):
        # meme espace de noms que les details (MeshAcc, along_box, side_frame...)
        circ_globals = dict(details_globals) if os.path.exists(DETAILS_PATH) else dict(globals())
        circ_globals["__file__"] = CIRCULATION_PATH
        exec(compile(open(CIRCULATION_PATH, encoding="utf-8").read(), CIRCULATION_PATH, "exec"), circ_globals)
        reports.extend(circ_globals["build_circulation"](levels))

    # Unites metriques
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0

    total = sum(len(c.objects) for c in set(_collections.values()))
    saved = None
    if save:
        try:
            os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
            bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
            saved = BLEND_PATH
        except Exception as exc:  # contexte restreint (serveur MCP) : on le signale sans planter
            saved = "NON SAUVE : %s" % exc
    return dict(objects=total, levels=stats, stairs=reports, blend=saved)


if __name__ == "__main__" or True:
    result = build_manor(save=True)
