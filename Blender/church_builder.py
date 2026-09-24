# -*- coding: utf-8 -*-
"""
church_builder.py — Eglise de village pour la foret 2 km (Forest_Landscape_2k).

Usage (dans Blender) :
    exec(open(r"...\\church_builder.py", encoding="utf-8").read())
    result = build_church()      # scene Blender "Church", collection CHURCH_ROOT
    result = export_church()     # -> Assets/_Game/Art/Church/Church.fbx

Conventions (identiques au manoir) :
  - metres, Z haut ; la facade (porte) regarde -Y, la nef s'etend vers +Y ;
  - origine = centre du seuil de la porte, au niveau du sol interieur (z = 0.3 : le
    socle depasse de 30 cm et s'enterre de 1.5 m pour epouser le terrain) ;
  - materiaux nommes comme ceux du projet Unity (M_Stone_Old, M_Roof_Tiles...) :
    le postprocesseur Unity les remplace par les materiaux URP existants ;
  - battants de porte "Church_DoorLeaf_L/R" : origine sur l'axe des gonds, le battant
    part vers le centre de la porte (L vers +X, R vers -X) ;
  - UV par projection boite en metres (add_box_uvs, comme manor_export.py).
"""

import math
import os

import bmesh
import bpy
from mathutils import Vector

FBX_PATH = r"C:\UnityGame\TheHouseOfSilence\Assets\_Game\Art\Church\Church.fbx"
SCENE_NAME = "Church"
ROOT = "CHURCH_ROOT"

FLOOR = 0.3            # sol interieur fini
NAVE_W = 12.0          # largeur exterieure de la nef
NAVE_L = 26.0          # longueur de la nef (y de 0 a 26)
WALL = 0.7             # epaisseur des murs de la nef
EAVE = 8.0             # hauteur des murs lateraux
RIDGE = 13.2           # faitage
TOWER_W = 6.4
TOWER_D = 6.0          # le clocher occupe y de -6 a 0
TOWER_H = 20.0
SPIRE_TOP = 31.0
DOOR_W = 2.4           # double porte : 2 x 1.2 m
DOOR_H = 3.6

COLORS = {
    "M_Stone_Old": (0.42, 0.40, 0.36),
    "M_Roof_Tiles": (0.20, 0.20, 0.23),
    "M_Tile_Old": (0.55, 0.52, 0.45),
    "M_Wood_Dark": (0.20, 0.12, 0.07),
    "M_Glass_Dirty": (0.35, 0.45, 0.55),
    "M_Metal_Rust": (0.35, 0.20, 0.12),
}


# ----------------------------------------------------------------------------
# Outils
# ----------------------------------------------------------------------------

def material(name):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
    col = COLORS.get(name, (0.5, 0.5, 0.5))
    mat.diffuse_color = (*col, 1.0)
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*col, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.85
    return mat


def new_object(name, bm, mat_names, coll, location=(0.0, 0.0, 0.0)):
    old = bpy.data.objects.get(name)
    if old is not None:
        bpy.data.objects.remove(old)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mat_names:
        me.materials.append(material(m))
    obj = bpy.data.objects.new(name, me)
    obj.location = location
    coll.objects.link(obj)
    return obj


def add_box(bm, x0, x1, y0, y1, z0, z1, mat_index=0):
    """Pave aligne sur les axes, ajoute au bmesh."""
    v = [bm.verts.new(p) for p in (
        (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
        (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1))]
    for f in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
        face = bm.faces.new([v[i] for i in f])
        face.material_index = mat_index
    return v


def box_object(name, x0, x1, y0, y1, z0, z1, mat, coll):
    bm = bmesh.new()
    add_box(bm, x0, x1, y0, y1, z0, z1)
    return new_object(name, bm, [mat], coll)


def add_prism(bm, profile, axis, n0, n1, mat_index=0):
    """Extrusion d'un profil 2D (u, z) le long de l'axe 'x' ou 'y', de n0 a n1."""
    def p3(u, z, n):
        return (n, u, z) if axis == "x" else (u, n, z)
    a = [bm.verts.new(p3(u, z, n0)) for u, z in profile]
    b = [bm.verts.new(p3(u, z, n1)) for u, z in profile]
    k = len(profile)
    faces = [bm.faces.new(a[::-1]), bm.faces.new(b)]
    for i in range(k):
        j = (i + 1) % k
        faces.append(bm.faces.new((a[i], a[j], b[j], b[i])))
    for f in faces:
        f.material_index = mat_index
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def arch_profile(uc, width, z0, spring, segs=14):
    """Baie en plein cintre : rectangle de z0 a spring, demi-cercle au-dessus."""
    r = width / 2.0
    pts = [(uc - r, z0), (uc + r, z0)]
    for i in range(segs + 1):
        a = math.pi * i / segs
        pts.append((uc + r * math.cos(a), spring + r * math.sin(a)))
    pts.append((uc - r, z0))
    # supprime les doublons (debut / fin de l'arc)
    clean = []
    for p in pts:
        if not clean or (abs(clean[-1][0] - p[0]) > 1e-6 or abs(clean[-1][1] - p[1]) > 1e-6):
            clean.append(p)
    if abs(clean[0][0] - clean[-1][0]) < 1e-6 and abs(clean[0][1] - clean[-1][1]) < 1e-6:
        clean.pop()
    return clean


def circle_profile(uc, zc, radius, segs=20):
    return [(uc + radius * math.cos(2 * math.pi * i / segs), zc + radius * math.sin(2 * math.pi * i / segs)) for i in range(segs)]


def half_disc_profile(uc, radius, z, segs=14):
    return [(uc + radius * math.cos(math.pi * i / segs), z + radius * math.sin(math.pi * i / segs)) for i in range(segs + 1)]


def cutter(name, profile, axis, n0, n1, coll):
    bm = bmesh.new()
    add_prism(bm, profile, axis, n0, n1)
    obj = new_object(name, bm, ["M_Stone_Old"], coll)
    obj.display_type = "WIRE"
    return obj


def apply_cuts(target, cutters, cut_coll):
    """Boolean EXACT avec une collection de decoupes, puis application (sans bpy.ops)."""
    for c in cutters:
        for col in list(c.users_collection):
            col.objects.unlink(c)
        cut_coll.objects.link(c)
    mod = target.modifiers.new("Cuts", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver = "EXACT"
    mod.operand_type = "COLLECTION"
    mod.collection = cut_coll
    dg = bpy.context.evaluated_depsgraph_get()
    dg.update()
    evaluated = target.evaluated_get(dg)
    new_mesh = bpy.data.meshes.new_from_object(evaluated)
    old = target.data
    target.modifiers.clear()
    target.data = new_mesh
    bpy.data.meshes.remove(old)
    for c in list(cut_coll.objects):
        bpy.data.objects.remove(c)


def add_box_uvs(mesh):
    """UV projection boite en metres (identique a manor_export.py)."""
    for layer in list(mesh.uv_layers):
        mesh.uv_layers.remove(layer)
    uv = mesh.uv_layers.new(name="UVMap")
    data = uv.data
    for poly in mesh.polygons:
        n = poly.normal
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        for li in poly.loop_indices:
            co = mesh.vertices[mesh.loops[li].vertex_index].co
            if az >= ax and az >= ay:
                data[li].uv = (co.x, co.y)
            elif ax >= ay:
                data[li].uv = (co.y, co.z)
            else:
                data[li].uv = (co.x, co.z)


# ----------------------------------------------------------------------------
# Construction
# ----------------------------------------------------------------------------

def build_church():
    scene = bpy.data.scenes.get(SCENE_NAME) or bpy.data.scenes.new(SCENE_NAME)
    scene.unit_settings.system = "METRIC"
    # Les booleens sont evalues dans la scene affichee : on affiche l'eglise.
    bpy.context.window.scene = scene

    old_root = bpy.data.collections.get(ROOT)
    if old_root is not None:
        for obj in list(old_root.all_objects):
            bpy.data.objects.remove(obj)
        for child in list(old_root.children_recursive):
            bpy.data.collections.remove(child)
        bpy.data.collections.remove(old_root)

    root = bpy.data.collections.new(ROOT)
    scene.collection.children.link(root)
    cuts = bpy.data.collections.new("CHURCH_Cuts")
    scene.collection.children.link(cuts)

    hw = NAVE_W / 2.0
    inner = hw - WALL
    tw = TOWER_W / 2.0
    ty0 = -TOWER_D
    objects = []

    # --- Socle (s'enterre de 1.5 m) et parvis -------------------------------
    bm = bmesh.new()
    add_box(bm, -hw - 0.3, hw + 0.3, -0.3, NAVE_L + 0.4, -1.5, FLOOR)
    add_box(bm, -tw - 0.3, tw + 0.3, ty0 - 0.3, 0.0, -1.5, FLOOR)
    add_box(bm, -2.4, 2.4, ty0 - 1.3, ty0 - 0.3, -1.5, FLOOR - 0.15)     # marche du parvis
    add_box(bm, -2.8, 2.8, ty0 - 2.2, ty0 - 1.3, -1.5, FLOOR - 0.30)
    objects.append(new_object("Church_Plinth", bm, ["M_Stone_Old"], root))

    # --- Sols ---------------------------------------------------------------
    bm = bmesh.new()
    add_box(bm, -inner, inner, WALL, NAVE_L, FLOOR, FLOOR + 0.04)                 # nef
    add_box(bm, -tw + 0.8, tw - 0.8, ty0 + 0.8, -0.8, FLOOR, FLOOR + 0.04)       # rez du clocher
    add_box(bm, -1.4, 1.4, -0.8, WALL, FLOOR, FLOOR + 0.04)                      # passage
    objects.append(new_object("Church_Floor", bm, ["M_Tile_Old"], root))

    # --- Murs de la nef ---------------------------------------------------
    windows_y = [5.0, 10.0, 15.0, 20.0]
    for side, sx in (("L", -1), ("R", 1)):
        x0, x1 = (-hw, -inner) if sx < 0 else (inner, hw)
        wall = box_object("Church_NaveWall_" + side, x0, x1, 0.0, NAVE_L, FLOOR, EAVE, "M_Stone_Old", root)
        cs = [cutter("cut_w%d" % i, arch_profile(y, 1.3, 2.4, 5.6), "x", x0 - 0.3, x1 + 0.3, root) for i, y in enumerate(windows_y)]
        apply_cuts(wall, cs, cuts)
        objects.append(wall)

        # vitraux
        bm = bmesh.new()
        xm = (x0 + x1) / 2.0
        for y in windows_y:
            add_prism(bm, arch_profile(y, 1.3, 2.4, 5.6), "x", xm - 0.03, xm + 0.03)
        objects.append(new_object("Church_Windows_" + side, bm, ["M_Glass_Dirty"], root))

        # contreforts
        bm = bmesh.new()
        bx0, bx1 = (-hw - 0.8, -hw) if sx < 0 else (hw, hw + 0.8)
        for y in (0.4, 7.5, 12.5, 17.5, NAVE_L - 0.4):
            add_box(bm, bx0, bx1, y - 0.45, y + 0.45, FLOOR - 0.3, 6.2)
            # glacis : petit chapeau incline en pierre
            add_prism(bm, [(y - 0.45, 6.2), (y + 0.45, 6.2), (y, 6.9)], "x", bx0, bx1)
        objects.append(new_object("Church_Buttresses_" + side, bm, ["M_Stone_Old"], root))

    # --- Facade (mur ouest de la nef) + pignon ---------------------------
    front = box_object("Church_NaveFront", -inner, inner, 0.0, WALL, FLOOR, EAVE, "M_Stone_Old", root)
    apply_cuts(front, [cutter("cut_pass", arch_profile(0.0, 2.8, FLOOR - 0.1, 4.4), "y", -1.2, WALL + 0.3, root)], cuts)
    objects.append(front)

    bm = bmesh.new()
    # pignon entre les murs lateraux, bord superieur sous le toit
    slope = (RIDGE - EAVE) / hw
    gable = [(-inner, EAVE), (inner, EAVE), (inner, EAVE + WALL * slope), (0.0, RIDGE - 0.05), (-inner, EAVE + WALL * slope)]
    add_prism(bm, gable, "y", 0.0, WALL)
    add_prism(bm, gable, "y", NAVE_L - WALL, NAVE_L)
    objects.append(new_object("Church_Gables", bm, ["M_Stone_Old"], root))

    # --- Chevet (mur est) ouvert sur l'abside ----------------------------
    back = box_object("Church_NaveBack", -inner, inner, NAVE_L - WALL, NAVE_L, FLOOR, EAVE, "M_Stone_Old", root)
    # sommet de l'arc (3.4 + 2.8 = 6.2 m) sous le haut du mur de l'abside (6.6 m) : pas de jour sous le toit
    apply_cuts(back, [cutter("cut_choir", arch_profile(0.0, 5.6, FLOOR - 0.1, 3.4), "y", NAVE_L - WALL - 0.3, NAVE_L + 0.3, root)], cuts)
    objects.append(back)

    # --- Abside semi-circulaire ------------------------------------------
    r_out, r_in, apse_h, segs = 4.4, 3.7, 6.6, 16
    bm = bmesh.new()
    rings = []
    for r in (r_out, r_in):
        low, high = [], []
        for i in range(segs + 1):
            a = math.pi * i / segs
            x, y = r * math.cos(a), NAVE_L + r * math.sin(a)
            low.append(bm.verts.new((x, y, FLOOR - 0.3)))
            high.append(bm.verts.new((x, y, apse_h)))
        rings.append((low, high))
    (ol, oh), (il, ih) = rings
    for i in range(segs):
        bm.faces.new((ol[i], ol[i + 1], oh[i + 1], oh[i]))
        bm.faces.new((il[i + 1], il[i], ih[i], ih[i + 1]))
        bm.faces.new((oh[i], oh[i + 1], ih[i + 1], ih[i]))
        bm.faces.new((ol[i + 1], ol[i], il[i], il[i + 1]))
    bm.faces.new((ol[0], oh[0], ih[0], il[0]))
    bm.faces.new((il[-1], ih[-1], oh[-1], ol[-1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    objects.append(new_object("Church_Apse", bm, ["M_Stone_Old"], root))

    # socle + sol surelevee du choeur (une marche)
    bm = bmesh.new()
    disc = half_disc_profile(0.0, r_out + 0.3, 0.0)
    verts_top = [bm.verts.new((u, NAVE_L + z, FLOOR)) for u, z in disc]
    verts_bot = [bm.verts.new((u, NAVE_L + z, -1.5)) for u, z in disc]
    bm.faces.new(verts_top)
    bm.faces.new(verts_bot[::-1])
    for i in range(len(disc) - 1):
        bm.faces.new((verts_bot[i], verts_bot[i + 1], verts_top[i + 1], verts_top[i]))
    bm.faces.new((verts_bot[-1], verts_bot[0], verts_top[0], verts_top[-1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    objects.append(new_object("Church_ApsePlinth", bm, ["M_Stone_Old"], root))

    bm = bmesh.new()
    add_box(bm, -3.4, 3.4, NAVE_L - 2.2, NAVE_L, FLOOR, FLOOR + 0.3)     # marche du choeur
    chord = half_disc_profile(0.0, r_in, 0.0)
    top = [bm.verts.new((u, NAVE_L + z, FLOOR + 0.3)) for u, z in chord]
    bot = [bm.verts.new((u, NAVE_L + z, FLOOR)) for u, z in chord]
    bm.faces.new(top)
    bm.faces.new(bot[::-1])
    for i in range(len(chord) - 1):
        bm.faces.new((bot[i], bot[i + 1], top[i + 1], top[i]))
    bm.faces.new((bot[-1], bot[0], top[0], top[-1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    objects.append(new_object("Church_Choir", bm, ["M_Tile_Old"], root))

    # toit de l'abside : demi-cone
    bm = bmesh.new()
    apex = bm.verts.new((0.0, NAVE_L, apse_h + 3.4))
    base = []
    for i in range(segs + 1):
        a = math.pi * i / segs
        base.append(bm.verts.new(((r_out + 0.4) * math.cos(a), NAVE_L + (r_out + 0.4) * math.sin(a), apse_h - 0.2)))
    for i in range(segs):
        bm.faces.new((base[i], base[i + 1], apex))
    bm.faces.new(base[::-1] + [])  # fond (dessous du toit)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    objects.append(new_object("Church_ApseRoof", bm, ["M_Roof_Tiles"], root))

    # --- Toiture de la nef (deux pans) -----------------------------------
    bm = bmesh.new()
    over, th = 0.7, 0.28
    y0r, y1r = -0.2, NAVE_L + 0.3
    for sx in (-1, 1):
        ex, ez = sx * (hw + over), EAVE - over * (RIDGE - EAVE) / hw
        rx, rz = 0.0, RIDGE
        p = [(ex, ez), (rx, rz), (rx, rz + th), (ex, ez + th)]
        v0 = [bm.verts.new((x, y0r, z)) for x, z in p]
        v1 = [bm.verts.new((x, y1r, z)) for x, z in p]
        fs = [bm.faces.new(v0[::-1]), bm.faces.new(v1)]
        for i in range(4):
            j = (i + 1) % 4
            fs.append(bm.faces.new((v0[i], v0[j], v1[j], v1[i])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    objects.append(new_object("Church_Roof", bm, ["M_Roof_Tiles"], root))

    # --- Clocher -------------------------------------------------------------
    t_wall = 0.8
    tower_parts = {
        "Front": (-tw, tw, ty0, ty0 + t_wall),
        "Back": (-tw, tw, -t_wall, 0.0),
        # murs lateraux entre facade et fond : aucune face coplanaire (pas de z-fighting)
        "L": (-tw, -tw + t_wall, ty0 + t_wall, -t_wall),
        "R": (tw - t_wall, tw, ty0 + t_wall, -t_wall),
    }
    belfry_z0, belfry_spring = 14.2, 16.4
    for key, (x0, x1, y0, y1) in tower_parts.items():
        wall = box_object("Church_Tower_" + key, x0, x1, y0, y1, FLOOR - 0.3, TOWER_H, "M_Stone_Old", root)
        cs = []
        if key in ("Front", "Back"):
            cs.append(cutter("cut_b" + key, arch_profile(0.0, 1.5, belfry_z0, belfry_spring), "y", y0 - 0.3, y1 + 0.3, root))
        else:
            cs.append(cutter("cut_b" + key, arch_profile((y0 + y1) / 2.0, 1.5, belfry_z0, belfry_spring), "x", x0 - 0.3, x1 + 0.3, root))
        if key == "Front":
            cs.append(cutter("cut_door", arch_profile(0.0, DOOR_W, FLOOR - 0.1, FLOOR + DOOR_H), "y", y0 - 0.3, y1 + 0.3, root))
            cs.append(cutter("cut_oculus", circle_profile(0.0, 9.2, 0.75), "y", y0 - 0.3, y1 + 0.3, root))
        if key == "Back":
            cs.append(cutter("cut_pass2", arch_profile(0.0, 2.8, FLOOR - 0.1, 4.4), "y", y0 - 0.3, y1 + 0.3, root))
        apply_cuts(wall, cs, cuts)
        objects.append(wall)

    # corniche, plancher du beffroi, fleche, croix
    bm = bmesh.new()
    add_box(bm, -tw - 0.35, tw + 0.35, ty0 - 0.35, 0.35, TOWER_H - 0.1, TOWER_H + 0.35)
    add_box(bm, -tw - 0.2, tw + 0.2, ty0 - 0.2, 0.2, 12.6, 12.9)             # cordon
    add_box(bm, -tw + t_wall, tw - t_wall, ty0 + t_wall, -t_wall, 13.6, 13.8)  # plancher du beffroi
    objects.append(new_object("Church_TowerTrim", bm, ["M_Stone_Old"], root))

    bm = bmesh.new()
    s = tw + 0.45
    cy = ty0 / 2.0
    apex = bm.verts.new((0.0, cy, SPIRE_TOP))
    corners = [bm.verts.new((x, cy + y, TOWER_H + 0.3)) for x, y in ((-s, -s), (s, -s), (s, s), (-s, s))]
    for i in range(4):
        bm.faces.new((corners[i], corners[(i + 1) % 4], apex))
    bm.faces.new(corners[::-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    objects.append(new_object("Church_Spire", bm, ["M_Roof_Tiles"], root))

    bm = bmesh.new()
    add_box(bm, -0.08, 0.08, cy - 0.08, cy + 0.08, SPIRE_TOP - 0.3, SPIRE_TOP + 2.2)
    add_box(bm, -0.6, 0.6, cy - 0.08, cy + 0.08, SPIRE_TOP + 1.2, SPIRE_TOP + 1.4)
    objects.append(new_object("Church_Cross", bm, ["M_Metal_Rust"], root))

    # abat-son (lames de bois dans les baies du beffroi)
    bm = bmesh.new()
    for key, (x0, x1, y0, y1) in tower_parts.items():
        for k in range(5):
            z = belfry_z0 + 0.35 + k * 0.42
            if key in ("Front", "Back"):
                add_box(bm, -0.72, 0.72, (y0 + y1) / 2 - 0.2, (y0 + y1) / 2 + 0.2, z, z + 0.05)
            else:
                ym = (y0 + y1) / 2
                add_box(bm, (x0 + x1) / 2 - 0.2, (x0 + x1) / 2 + 0.2, ym - 0.72, ym + 0.72, z, z + 0.05)
    objects.append(new_object("Church_Louvres", bm, ["M_Wood_Dark"], root))

    # --- Tympan au-dessus de la porte (fixe) -------------------------------
    door_y = ty0 + t_wall / 2.0
    bm = bmesh.new()
    add_prism(bm, half_disc_profile(0.0, DOOR_W / 2.0, FLOOR + DOOR_H), "y", door_y - 0.05, door_y + 0.05)
    objects.append(new_object("Church_Tympanum", bm, ["M_Wood_Dark"], root))

    # --- Battants de porte (origine = gond) --------------------------------
    leaf_w = DOOR_W / 2.0 - 0.01
    for side, sx in (("L", 1), ("R", -1)):
        bm = bmesh.new()
        xa, xb = (0.0, leaf_w) if sx > 0 else (-leaf_w, 0.0)
        add_box(bm, xa, xb, -0.05, 0.05, 0.005, DOOR_H - 0.01, 0)
        # pentures en fer, cote exterieur (-Y) et interieur
        for z in (0.5, 1.8, 3.1):
            add_box(bm, xa + 0.02, xb - 0.02, -0.075, -0.05, z, z + 0.09, 1)
            add_box(bm, xa + 0.02, xb - 0.02, 0.05, 0.075, z, z + 0.09, 1)
        # anneau de tirage pres du bord libre
        hx = (xb - 0.18) if sx > 0 else (xa + 0.18)
        add_box(bm, hx - 0.05, hx + 0.05, -0.11, -0.05, 1.55, 1.65, 1)
        add_box(bm, hx - 0.09, hx + 0.09, -0.12, -0.10, 1.30, 1.34, 1)
        add_box(bm, hx - 0.09, hx - 0.07, -0.12, -0.10, 1.30, 1.55, 1)
        add_box(bm, hx + 0.07, hx + 0.09, -0.12, -0.10, 1.30, 1.55, 1)
        leaf = new_object("Church_DoorLeaf_" + side, bm, ["M_Wood_Dark", "M_Metal_Rust"], root,
                          location=(-sx * DOOR_W / 2.0, door_y, FLOOR))
        leaf["hos_type"] = "door"
        leaf["hos_width"] = round(leaf_w, 3)
        objects.append(leaf)

    # --- Mobilier : bancs, autel -------------------------------------------
    bm = bmesh.new()
    for row in range(7):
        y = 4.0 + row * 2.3
        for sx in (-1, 1):
            xa, xb = (0.8, 4.3) if sx > 0 else (-4.3, -0.8)
            add_box(bm, xa, xb, y, y + 0.42, FLOOR + 0.42, FLOOR + 0.48)          # assise
            add_box(bm, xa, xb, y + 0.38, y + 0.44, FLOOR + 0.48, FLOOR + 0.98)   # dossier
            for ex in (xa, xb - 0.06):
                add_box(bm, ex, ex + 0.06, y - 0.02, y + 0.46, FLOOR, FLOOR + 1.0)  # joues
    objects.append(new_object("Church_Pews", bm, ["M_Wood_Dark"], root))

    bm = bmesh.new()
    add_box(bm, -1.3, 1.3, NAVE_L + 1.2, NAVE_L + 2.2, FLOOR + 0.3, FLOOR + 1.3)
    add_box(bm, -1.45, 1.45, NAVE_L + 1.1, NAVE_L + 2.3, FLOOR + 1.3, FLOOR + 1.42)
    objects.append(new_object("Church_Altar", bm, ["M_Stone_Old"], root))

    bm = bmesh.new()
    add_box(bm, -0.04, 0.04, NAVE_L + 1.65, NAVE_L + 1.75, FLOOR + 1.42, FLOOR + 2.3)
    add_box(bm, -0.3, 0.3, NAVE_L + 1.65, NAVE_L + 1.75, FLOOR + 1.95, FLOOR + 2.03)
    objects.append(new_object("Church_AltarCross", bm, ["M_Metal_Rust"], root))

    # --- Racine d'export, UV ---------------------------------------------
    root_empty = bpy.data.objects.new("Church", None)
    root_empty.empty_display_type = "PLAIN_AXES"
    root_empty["hos_type"] = "group"
    root.objects.link(root_empty)
    for obj in objects:
        obj.parent = root_empty
        add_box_uvs(obj.data)
        for p in obj.data.polygons:
            p.use_smooth = False

    bpy.data.collections.remove(cuts)

    tris = sum(len(p.vertices) - 2 for o in objects for p in o.data.polygons)
    return dict(objects=len(objects), triangles=tris, scene=scene.name)


def export_church(path=FBX_PATH):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    scene = bpy.data.scenes[SCENE_NAME]
    view_layer = scene.view_layers[0]
    window = bpy.context.window
    previous = window.scene
    window.scene = scene
    try:
        for obj in scene.objects:
            obj.select_set(False, view_layer=view_layer)
        root = bpy.data.collections[ROOT]
        for obj in list(root.all_objects):
            if obj is None:
                continue
            obj.hide_set(False, view_layer=view_layer)
            obj.select_set(True, view_layer=view_layer)
        with bpy.context.temp_override(window=window, scene=scene, view_layer=view_layer):
            bpy.ops.export_scene.fbx(
                filepath=path,
                use_selection=True,
                object_types={"MESH", "EMPTY"},
                axis_forward="-Z",
                axis_up="Y",
                global_scale=1.0,
                apply_unit_scale=True,
                apply_scale_options="FBX_SCALE_ALL",
                bake_space_transform=False,      # conversion d'axes faite par Unity (bakeAxisConversion)
                use_mesh_modifiers=True,
                mesh_smooth_type="OFF",
                use_custom_props=True,
                add_leaf_bones=False,
                bake_anim=False,
                path_mode="AUTO",
                embed_textures=False,
                use_triangles=False,
                batch_mode="OFF",
            )
    finally:
        window.scene = previous
    size = os.path.getsize(path) if os.path.exists(path) else 0
    return dict(fbx=path, size_kb=round(size / 1024))
