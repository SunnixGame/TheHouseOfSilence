# -*- coding: utf-8 -*-
"""
cemetery_builder.py — Vieux cimetiere de la foret (Forest_Landscape_2k).

Usage (dans Blender) :
    exec(open(r"...\\cemetery_builder.py", encoding="utf-8").read())
    result = build_cemetery()     # scene Blender "Cemetery", collection CEMETERY_ROOT
    result = export_cemetery()    # -> Assets/_Game/Art/Cemetery/Cemetery.fbx

Memes conventions que church_builder.py (dont il reutilise les outils) :
  - metres, Z haut ; la grille d'entree regarde -Y (y = 0), l'enclos s'etend vers +Y ;
  - origine = centre du seuil de la grille, au niveau du sol ;
  - materiaux nommes comme ceux du projet Unity ;
  - battants "Cemetery_GateLeaf_L/R" : origine sur l'axe des gonds, battant vers le centre ;
  - tout est deterministe (graine fixe) : relancer redonne le meme cimetiere.
"""

import importlib
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

BLENDER_DIR = r"C:\UnityGame\TheHouseOfSilence\Blender"
if BLENDER_DIR not in sys.path:
    sys.path.insert(0, BLENDER_DIR)
import church_builder as cb  # noqa: E402  (outils : boites, prismes, arcs, UV, materiaux)
importlib.reload(cb)

FBX_PATH = r"C:\UnityGame\TheHouseOfSilence\Assets\_Game\Art\Cemetery\Cemetery.fbx"
SCENE_NAME = "Cemetery"
ROOT = "CEMETERY_ROOT"

HALF_W = 14.0        # enclos : x de -14 a 14
DEPTH = 30.0         # y de 0 a 30
WALL_H = 1.25
WALL_T = 0.5
GATE_W = 3.0         # deux battants de 1.5 m
GATE_H = 2.3
SEED = 1313

cb.COLORS.update({
    "M_Ground": (0.12, 0.10, 0.07),
    "M_Concrete_Wet": (0.25, 0.26, 0.25),
})


# ----------------------------------------------------------------------------
# Outils
# ----------------------------------------------------------------------------

def transformed(bm, matrix, build):
    """Construit une piece dans le bmesh puis lui applique une matrice (inclinaison, position)."""
    bm.verts.ensure_lookup_table()
    start = len(bm.verts)
    build()
    bm.verts.ensure_lookup_table()
    new = [bm.verts[i] for i in range(start, len(bm.verts))]
    bmesh.ops.transform(bm, matrix=matrix, verts=new)


def frustum(bm, w0, d0, w1, d1, z0, z1):
    """Tronc de pyramide centre (base w0 x d0, sommet w1 x d1)."""
    b = [bm.verts.new((x * w0 / 2, y * d0 / 2, z0)) for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    t = [bm.verts.new((x * w1 / 2, y * d1 / 2, z1)) for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    faces = [bm.faces.new(b[::-1]), bm.faces.new(t)]
    for i in range(4):
        j = (i + 1) % 4
        faces.append(bm.faces.new((b[i], b[j], t[j], t[i])))
    return faces


def pyramid(bm, w, d, z0, z1):
    b = [bm.verts.new((x * w / 2, y * d / 2, z0)) for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    apex = bm.verts.new((0.0, 0.0, z1))
    faces = [bm.faces.new(b[::-1])]
    for i in range(4):
        faces.append(bm.faces.new((b[i], b[(i + 1) % 4], apex)))
    return faces


def finish(bm):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


# ----------------------------------------------------------------------------
# Tombes
# ----------------------------------------------------------------------------

def headstone(bm, rng, kind, height):
    """Une pierre tombale centree sur l'origine, face vers -Y."""
    if kind == "arch":
        w = rng.uniform(0.55, 0.8)
        cb.add_prism(bm, cb.arch_profile(0.0, w, 0.0, max(0.2, height - w / 2)), "y", -0.08, 0.08)
    elif kind == "slab":
        w = rng.uniform(0.5, 0.75)
        cb.add_box(bm, -w / 2, w / 2, -0.07, 0.07, 0.0, height)
        cb.add_box(bm, -w / 2 - 0.04, w / 2 + 0.04, -0.1, 0.1, height, height + 0.06)  # chaperon
    elif kind == "cross":
        cb.add_box(bm, -0.3, 0.3, -0.18, 0.18, 0.0, 0.25)                              # socle
        cb.add_box(bm, -0.07, 0.07, -0.07, 0.07, 0.25, 0.25 + height)
        arm = 0.25 + height * 0.72
        cb.add_box(bm, -0.32, 0.32, -0.07, 0.07, arm - 0.07, arm + 0.07)
    elif kind == "obelisk":
        cb.add_box(bm, -0.35, 0.35, -0.35, 0.35, 0.0, 0.3)
        frustum(bm, 0.36, 0.36, 0.22, 0.22, 0.3, 0.3 + height * 1.4)
        pyramid(bm, 0.22, 0.22, 0.3 + height * 1.4, 0.45 + height * 1.4)
    elif kind == "broken":
        w = rng.uniform(0.55, 0.75)
        h = height * rng.uniform(0.35, 0.6)
        # cassure en biais : haut du profil irregulier
        cb.add_prism(bm, [(-w / 2, 0.0), (w / 2, 0.0), (w / 2, h * 0.7), (w * 0.1, h), (-w * 0.2, h * 0.8), (-w / 2, h * 0.95)], "y", -0.08, 0.08)


def build_graves(root):
    rng = random.Random(SEED)
    stones, kerbs, mounds = bmesh.new(), bmesh.new(), bmesh.new()
    kinds = ["arch", "arch", "slab", "cross", "cross", "obelisk", "broken"]
    count = 0

    rows = [4.5 + i * 2.9 for i in range(7)]            # y de 4.5 a 21.9
    cols = [-12.0, -9.2, -6.4, -3.6, 3.6, 6.4, 9.2, 12.0]  # allee centrale libre (|x| < 2)

    for y in rows:
        for x in cols:
            if rng.random() < 0.18:
                continue  # emplacements vides : pas de grille parfaite
            gx = x + rng.uniform(-0.35, 0.35)
            gy = y + rng.uniform(-0.25, 0.25)
            yaw = math.radians(rng.uniform(-6, 6))
            kind = rng.choice(kinds)
            height = rng.uniform(0.7, 1.15)
            tilt_x = math.radians(rng.uniform(-9, 9) if rng.random() < 0.45 else rng.uniform(-2, 2))
            tilt_y = math.radians(rng.uniform(-6, 6) if rng.random() < 0.3 else 0.0)
            sink = rng.uniform(0.0, 0.12)

            # pierre en tete de tombe (cote -Y de la sepulture), un peu enfoncee et penchee
            m = (Matrix.Translation((gx, gy, -sink)) @ Matrix.Rotation(yaw, 4, "Z")
                 @ Matrix.Rotation(tilt_x, 4, "X") @ Matrix.Rotation(tilt_y, 4, "Y"))
            transformed(stones, m, lambda: headstone(stones, rng, kind, height))

            # sepulture derriere la pierre : bordure de pierre ou tertre de terre
            body = Matrix.Translation((gx, gy + 1.1, 0.0)) @ Matrix.Rotation(yaw, 4, "Z")
            if rng.random() < 0.55:
                def kerb():
                    cb.add_box(kerbs, -0.5, 0.5, -0.9, -0.78, -0.1, 0.18)
                    cb.add_box(kerbs, -0.5, 0.5, 0.78, 0.9, -0.1, 0.18)
                    cb.add_box(kerbs, -0.5, -0.38, -0.78, 0.78, -0.1, 0.18)
                    cb.add_box(kerbs, 0.38, 0.5, -0.78, 0.78, -0.1, 0.18)
                    if rng.random() < 0.5:
                        cb.add_box(kerbs, -0.38, 0.38, -0.78, 0.78, -0.1, 0.12)  # dalle couvrante
                transformed(kerbs, body, kerb)
            else:
                transformed(mounds, body, lambda: frustum(mounds, 0.95, 1.8, 0.55, 1.35, -0.1, rng.uniform(0.12, 0.25)))
            count += 1

    objs = [
        cb.new_object("Cemetery_Headstones", finish(stones), ["M_Stone_Old"], root),
        cb.new_object("Cemetery_GraveKerbs", finish(kerbs), ["M_Concrete_Wet"], root),
        cb.new_object("Cemetery_GraveMounds", finish(mounds), ["M_Ground"], root),
    ]
    return objs, count


# ----------------------------------------------------------------------------
# Enclos, grille, calvaire, mausolee
# ----------------------------------------------------------------------------

def build_enclosure(root):
    objs = []
    hw, t = HALF_W, WALL_T
    gate = GATE_W / 2 + 0.45  # demi-ouverture + pilier de la grille

    bm = bmesh.new()
    cb.add_box(bm, -hw, -gate, 0.0, t, -0.4, WALL_H)                 # facade gauche
    cb.add_box(bm, gate, hw, 0.0, t, -0.4, WALL_H)                   # facade droite
    cb.add_box(bm, -hw, hw, DEPTH - t, DEPTH, -0.4, WALL_H)           # fond
    cb.add_box(bm, -hw, -hw + t, t, DEPTH - t, -0.4, WALL_H)          # cote gauche
    cb.add_box(bm, hw - t, hw, t, DEPTH - t, -0.4, WALL_H)            # cote droit
    objs.append(cb.new_object("Cemetery_Walls", finish(bm), ["M_Stone_Old"], root))

    # couvertine sur le mur + piliers reguliers (quelques pierres manquent : ruine)
    rng = random.Random(SEED + 1)
    bm = bmesh.new()
    segments = [(-hw, -gate, 0.0, t), (gate, hw, 0.0, t), (-hw, hw, DEPTH - t, DEPTH),
                (-hw, -hw + t, t, DEPTH - t), (hw - t, hw, t, DEPTH - t)]
    for x0, x1, y0, y1 in segments:
        cb.add_box(bm, x0 - 0.06, x1 + 0.06, y0 - 0.06, y1 + 0.06, WALL_H, WALL_H + 0.1)
    pillars = []
    for x in [-hw + 0.25 + i * 4.0 for i in range(8)]:
        if abs(x) > gate + 0.5:
            pillars.append((x, t / 2))
        pillars.append((x, DEPTH - t / 2))
    for y in [t + 3.5 + i * 4.0 for i in range(7)]:
        pillars += [(-hw + t / 2, y), (hw - t / 2, y)]
    for px, py in pillars:
        if rng.random() < 0.12:
            continue
        cb.add_box(bm, px - 0.38, px + 0.38, py - 0.38, py + 0.38, -0.4, WALL_H + 0.35)
        cb.add_box(bm, px - 0.44, px + 0.44, py - 0.44, py + 0.44, WALL_H + 0.35, WALL_H + 0.45)
    objs.append(cb.new_object("Cemetery_WallTrim", finish(bm), ["M_Stone_Old"], root))

    # grands piliers de la grille, chapeaux pyramidaux
    bm = bmesh.new()
    for sx in (-1, 1):
        cx = sx * (GATE_W / 2 + 0.45)
        cb.add_box(bm, cx - 0.45, cx + 0.45, -0.2, t + 0.2, -0.4, 2.7)
        transformed(bm, Matrix.Translation((cx, t / 2, 0.0)), lambda: (frustum(bm, 1.05, 1.05, 1.05, 1.05, 2.7, 2.85), pyramid(bm, 1.05, 1.05, 2.85, 3.35)))
    objs.append(cb.new_object("Cemetery_GatePillars", finish(bm), ["M_Stone_Old"], root))

    # battants de grille en fer forge (origine = gond)
    leaf_w = GATE_W / 2 - 0.02
    for side, sx in (("L", 1), ("R", -1)):
        bm = bmesh.new()
        xa, xb = (0.0, leaf_w) if sx > 0 else (-leaf_w, 0.0)
        cb.add_box(bm, xa, xb, -0.03, 0.03, 0.08, 0.16)                    # traverse basse
        cb.add_box(bm, xa, xb, -0.03, 0.03, 1.1, 1.18)                     # traverse milieu
        cb.add_box(bm, xa, xb, -0.03, 0.03, GATE_H - 0.3, GATE_H - 0.22)   # traverse haute
        cb.add_box(bm, xa, xa + 0.06, -0.035, 0.035, 0.05, GATE_H)         # montants
        cb.add_box(bm, xb - 0.06, xb, -0.035, 0.035, 0.05, GATE_H)
        n = 9
        for k in range(1, n):
            bx = xa + (xb - xa) * k / n
            top = GATE_H - 0.1 + 0.18 * math.sin(math.pi * k / n)          # sommet en arc
            cb.add_box(bm, bx - 0.012, bx + 0.012, -0.012, 0.012, 0.1, top)
            transformed(bm, Matrix.Translation((bx, 0.0, top)), lambda: pyramid(bm, 0.07, 0.07, 0.0, 0.14))  # pointes
        leaf = cb.new_object("Cemetery_GateLeaf_" + side, finish(bm), ["M_Metal_Rust"], root,
                             location=(-sx * GATE_W / 2, t / 2, 0.0))
        leaf["hos_type"] = "door"
        objs.append(leaf)

    # allee centrale gravillonnee
    bm = bmesh.new()
    cb.add_box(bm, -1.3, 1.3, -1.5, 22.8, -0.1, 0.04)
    cb.add_box(bm, -1.1, 1.1, 22.8, 24.2, -0.1, 0.04)
    objs.append(cb.new_object("Cemetery_Path", finish(bm), ["M_Concrete_Wet"], root))
    return objs


def build_calvary(root):
    """Grand calvaire au croisement de l'allee."""
    bm = bmesh.new()
    y = 13.2
    cb.add_box(bm, -1.2, 1.2, y - 1.2, y + 1.2, -0.2, 0.3)
    cb.add_box(bm, -0.9, 0.9, y - 0.9, y + 0.9, 0.3, 0.6)
    transformed(bm, Matrix.Translation((0.0, y, 0.0)), lambda: frustum(bm, 0.8, 0.8, 0.55, 0.55, 0.6, 2.0))
    cb.add_box(bm, -0.13, 0.13, y - 0.13, y + 0.13, 2.0, 5.2)
    cb.add_box(bm, -0.9, 0.9, y - 0.13, y + 0.13, 4.1, 4.36)
    return [cb.new_object("Cemetery_Calvary", finish(bm), ["M_Stone_Old"], root)]


def build_mausoleum(root):
    """Petit mausolee au fond de l'allee : colonnes, fronton, porte de fer (fermee)."""
    objs = []
    y0, y1, hw, h = 24.4, 29.3, 2.6, 3.6
    bm = bmesh.new()
    cb.add_box(bm, -hw - 0.3, hw + 0.3, y0 - 0.6, y1 + 0.1, -0.3, 0.35)        # emmarchement
    cb.add_box(bm, -hw, hw, y0 + 0.9, y1, 0.35, h)                             # cella
    for sx in (-1, 1):
        for cx in (sx * (hw - 0.3), sx * (hw - 1.3)):
            cb.add_box(bm, cx - 0.17, cx + 0.17, y0 - 0.17 + 0.25, y0 + 0.17 + 0.25, 0.35, h)  # colonnes
    cb.add_box(bm, -hw - 0.15, hw + 0.15, y0, y1 + 0.05, h, h + 0.3)            # entablement
    cb.add_prism(bm, [(-hw - 0.15, h + 0.3), (hw + 0.15, h + 0.3), (0.0, h + 1.5)], "y", y0 - 0.02, y0 + 0.3)  # fronton
    objs.append(cb.new_object("Cemetery_Mausoleum", finish(bm), ["M_Stone_Old"], root))

    bm = bmesh.new()
    over = 0.25
    for sx in (-1, 1):
        ex, ez = sx * (hw + over), h + 0.3 - over * 1.2 / hw
        p = [(ex, ez), (0.0, h + 1.5), (0.0, h + 1.68), (ex, ez + 0.18)]
        v0 = [bm.verts.new((x, y0 + 0.3, z)) for x, z in p]
        v1 = [bm.verts.new((x, y1 + 0.2, z)) for x, z in p]
        bm.faces.new(v0[::-1]); bm.faces.new(v1)
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((v0[i], v0[j], v1[j], v1[i]))
    objs.append(cb.new_object("Cemetery_MausoleumRoof", finish(bm), ["M_Roof_Tiles"], root))

    bm = bmesh.new()
    cb.add_prism(bm, cb.arch_profile(0.0, 1.3, 0.35, 2.3), "y", y0 + 0.85, y0 + 0.9)
    for k in range(-2, 3):
        cb.add_box(bm, k * 0.22 - 0.015, k * 0.22 + 0.015, y0 + 0.8, y0 + 0.85, 0.4, 2.7)
    objs.append(cb.new_object("Cemetery_MausoleumDoor", finish(bm), ["M_Metal_Rust"], root))
    return objs


# ----------------------------------------------------------------------------

def build_cemetery():
    scene = bpy.data.scenes.get(SCENE_NAME) or bpy.data.scenes.new(SCENE_NAME)
    scene.unit_settings.system = "METRIC"
    bpy.context.window.scene = scene

    old = bpy.data.collections.get(ROOT)
    if old is not None:
        for obj in list(old.all_objects):
            bpy.data.objects.remove(obj)
        bpy.data.collections.remove(old)
    root = bpy.data.collections.new(ROOT)
    scene.collection.children.link(root)

    objects = []
    objects += build_enclosure(root)
    graves, grave_count = build_graves(root)
    objects += graves
    objects += build_calvary(root)
    objects += build_mausoleum(root)

    root_empty = bpy.data.objects.new("Cemetery", None)
    root_empty.empty_display_type = "PLAIN_AXES"
    root_empty["hos_type"] = "group"
    root.objects.link(root_empty)
    for obj in objects:
        obj.parent = root_empty
        cb.add_box_uvs(obj.data)
        for p in obj.data.polygons:
            p.use_smooth = False

    tris = sum(len(p.vertices) - 2 for o in objects for p in o.data.polygons)
    return dict(objects=len(objects), graves=grave_count, triangles=tris)


def export_cemetery(path=FBX_PATH):
    saved = cb.SCENE_NAME, cb.ROOT
    cb.SCENE_NAME, cb.ROOT = SCENE_NAME, ROOT
    try:
        return cb.export_church(path)
    finally:
        cb.SCENE_NAME, cb.ROOT = saved
