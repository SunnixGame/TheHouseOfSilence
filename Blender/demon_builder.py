"""
Demon - modele procedural + squelette complet pour l'animation (Unity Humanoid).

Execution dans Blender (scene "Demon") :
    import sys; sys.path.insert(0, r"C:/UnityGame/TheHouseOfSilence/Blender")
    import demon_builder as db; db.build_demon(); db.export_demon()

Corps : squelette de sommets + modificateur Skin (maillage organique en quads),
subdivise, puis sculpte par code (cotes, vertebres, orbites, rides). Cornes, griffes,
dents, epines et yeux sont des pieces a part, pesees a 100 % sur leur os puis jointes.

Squelette (Z haut, le demon regarde vers -Y) : noms compatibles Unity Humanoid,
cotes gauche/droit en .L / .R pour le miroir de pose dans Blender.
    Hips > Spine > Chest > UpperChest > Neck > Head > Jaw, Eye.L/R
    UpperChest > Shoulder > UpperArm (epaule) > LowerArm (coude) > Hand (poignet)
        > Thumb/Index/Middle/Ring/Little  Proximal > Intermediate > Distal
    Hips > UpperLeg (hanche) > LowerLeg (genou) > Foot (cheville) > Toes
        > ToeInner/ToeMiddle/ToeOuter 1 > 2 ; Foot > Heel (ergot)
    Hips > Tail_01 ... Tail_08
Controleurs (non exportes) : IK_Foot, Pole_Knee, IK_Hand, Pole_Elbow.
"""

import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

SCENE_NAME = "Demon"
FBX_PATH = r"C:/UnityGame/TheHouseOfSilence/Assets/_Game/Art/Demon/Demon.fbx"

# ----------------------------------------------------------------------
# Proportions (m). Demon de 2,3 m, maigre, voute, bras tres longs.
# ----------------------------------------------------------------------

J = {
    # axe
    "hips": (0.0, 0.0, 1.10),
    "spine": (0.0, -0.01, 1.26),
    "chest": (0.0, -0.03, 1.44),
    "upperchest": (0.0, -0.06, 1.62),
    "neck": (0.0, -0.09, 1.80),
    "head": (0.0, -0.15, 1.95),
    "headtop": (0.0, -0.15, 2.19),
    "jaw": (0.0, -0.16, 2.00),
    "jawtip": (0.0, -0.31, 1.93),
    "eye": (0.045, -0.255, 2.06),
    # bras (cote gauche, +X)
    "clavicle": (0.07, -0.06, 1.71),
    "shoulder": (0.2, -0.05, 1.68),
    "elbow": (0.28, -0.01, 1.32),
    "wrist": (0.335, -0.09, 0.97),
    "palm": (0.35, -0.11, 0.88),
    # jambe
    "hip": (0.115, 0.0, 1.07),
    "knee": (0.15, -0.05, 0.60),
    "ankle": (0.16, 0.04, 0.10),
    "ball": (0.17, -0.12, 0.035),
    "toetip": (0.17, -0.23, 0.01),
    "heel": (0.16, 0.10, 0.02),
}

# Doigts : (nom, depart relatif a la paume, direction, longueurs des 3 phalanges)
FINGERS = [
    ("Index", (0.035, -0.035, 0.0), (0.12, -0.35, -1.0), (0.075, 0.055, 0.045)),
    ("Middle", (0.012, -0.012, -0.01), (0.02, -0.2, -1.0), (0.085, 0.062, 0.05)),
    ("Ring", (-0.012, 0.012, -0.005), (-0.08, -0.05, -1.0), (0.08, 0.058, 0.046)),
    ("Little", (-0.03, 0.03, 0.0), (-0.2, 0.05, -1.0), (0.065, 0.048, 0.04)),
]
THUMB = ("Thumb", (0.01, -0.05, 0.05), (0.15, -1.0, -0.7), (0.05, 0.045, 0.04))

# Orteils griffus : (nom, decalage X a la base, direction, longueurs)
TOES = [
    ("ToeInner", -0.03, (-0.15, -1.0, -0.15), (0.055, 0.045)),
    ("ToeMiddle", 0.0, (0.0, -1.0, -0.15), (0.065, 0.05)),
    ("ToeOuter", 0.032, (0.18, -1.0, -0.15), (0.05, 0.04)),
]

TAIL_SEGMENTS = 8


def v(p):
    return Vector(p)


def mirror(p):
    return Vector((-p[0], p[1], p[2]))


def tail_points():
    """Queue : part du bas du dos, descend en arriere puis remonte en crochet."""
    pts = []
    for i in range(TAIL_SEGMENTS + 1):
        t = i / TAIL_SEGMENTS
        y = 0.10 + 1.25 * t
        z = 1.05 - 0.75 * math.sin(t * math.pi * 0.62) + 0.25 * t * t
        x = 0.12 * math.sin(t * math.pi * 1.3)
        pts.append(Vector((x, y, z)))
    return pts


def finger_chain(base, direction, lengths, curl=0.18):
    """Points d'un doigt qui se recourbe vers la paume (-Y) a chaque phalange."""
    d = v(direction).normalized()
    pts = [base.copy()]
    p = base.copy()
    for i, length in enumerate(lengths):
        bend = Vector((0.0, curl * (i + 1) * 0.5, 0.0))
        d = (d + bend * 0.4).normalized() if i > 0 else d
        p = p + d * length
        pts.append(p.copy())
    return pts


# ----------------------------------------------------------------------
# Scene / materiaux
# ----------------------------------------------------------------------

def get_scene():
    scene = bpy.data.scenes.get(SCENE_NAME)
    if scene is None:
        scene = bpy.data.scenes.new(SCENE_NAME)
    bpy.context.window.scene = scene
    scene.unit_settings.system = "METRIC"
    return scene


def clear_scene(scene):
    for obj in list(scene.collection.all_objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for coll in list(scene.collection.children):
        bpy.data.collections.remove(coll)
    # Donnees orphelines d'un build precedent (sinon les noms prennent un .001)
    for datablocks in (bpy.data.meshes, bpy.data.armatures, bpy.data.cameras, bpy.data.lights):
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)
    bpy.context.view_layer.update()


def material(name, color, roughness, emission=None, subsurface=0.0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    bsdf = next((n for n in nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf is None:
        out = next((n for n in nodes if n.type == "OUTPUT_MATERIAL"), None) or nodes.new("ShaderNodeOutputMaterial")
        bsdf = nodes.new("ShaderNodeBsdfPrincipled")
        mat.node_tree.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    if "Subsurface Weight" in bsdf.inputs:
        bsdf.inputs["Subsurface Weight"].default_value = subsurface
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 4.0
    mat.diffuse_color = (*color, 1.0)
    return mat


def materials():
    return {
        "skin": material("M_Demon_Skin", (0.16, 0.045, 0.035), 0.62, subsurface=0.08),
        "horn": material("M_Demon_Horn", (0.05, 0.04, 0.035), 0.4),
        "claw": material("M_Demon_Claw", (0.03, 0.025, 0.02), 0.3),
        "teeth": material("M_Demon_Teeth", (0.55, 0.5, 0.38), 0.35),
        "eye": material("M_Demon_Eye", (1.0, 0.55, 0.1), 0.2, emission=(1.0, 0.35, 0.05)),
    }


# ----------------------------------------------------------------------
# Corps : graphe de sommets + Skin
# ----------------------------------------------------------------------

class SkinGraph:
    def __init__(self):
        self.verts = []
        self.radii = []
        self.edges = []

    def add(self, p, r):
        self.verts.append(Vector(p))
        self.radii.append(r if isinstance(r, tuple) else (r, r))
        return len(self.verts) - 1

    def chain(self, start_index, points, radii, steps=2):
        """Relie start_index a une suite de points, en subdivisant chaque troncon."""
        prev = start_index
        prev_p = self.verts[start_index]
        prev_r = self.radii[start_index]
        for p, r in zip(points, radii):
            p = Vector(p)
            r = r if isinstance(r, tuple) else (r, r)
            for s in range(1, steps + 1):
                t = s / steps
                q = prev_p.lerp(p, t)
                rr = (prev_r[0] + (r[0] - prev_r[0]) * t, prev_r[1] + (r[1] - prev_r[1]) * t)
                idx = self.add(q, rr)
                self.edges.append((prev, idx))
                prev = idx
            prev_p, prev_r = p, r
        return prev


def build_body_graph():
    g = SkinGraph()
    hips = g.add(J["hips"], (0.16, 0.12))

    # Colonne -> tete (maigre a la taille, cage thoracique large)
    waist = g.chain(hips, [J["spine"]], [(0.115, 0.095)])
    chest = g.chain(waist, [J["chest"]], [(0.17, 0.12)])
    upper = g.chain(chest, [J["upperchest"]], [(0.19, 0.125)])
    neck_base = g.chain(upper, [(0.0, -0.07, 1.71)], [(0.13, 0.1)], steps=1)
    neck = g.chain(neck_base, [J["neck"]], [(0.06, 0.055)])
    head = g.chain(neck, [J["head"]], [(0.075, 0.08)])
    g.chain(head, [(0.0, -0.16, 2.07), J["headtop"]], [(0.12, 0.125), (0.08, 0.08)])
    # Machoire allongee
    g.chain(head, [(0.0, -0.22, 1.99), J["jawtip"]], [(0.085, 0.07), (0.04, 0.035)])

    for side in (1, -1):
        def s(p):
            p = Vector(p)
            return Vector((p.x * side, p.y, p.z))

        # Bras : clavicule, epaule, coude, poignet, paume
        clav = g.chain(neck_base, [s(J["clavicle"])], [(0.085, 0.075)], steps=1)
        sh = g.chain(clav, [s(J["shoulder"])], [(0.085, 0.08)])
        mid_up = s(v(J["shoulder"]).lerp(v(J["elbow"]), 0.45))
        el = g.chain(sh, [mid_up, s(J["elbow"])], [(0.058, 0.055), (0.042, 0.04)])
        mid_fore = s(v(J["elbow"]).lerp(v(J["wrist"]), 0.3))
        wr = g.chain(el, [mid_fore, s(J["wrist"])], [(0.048, 0.042), (0.03, 0.025)])
        palm = g.chain(wr, [s(J["palm"])], [(0.042, 0.02)], steps=1)

        for name, off, direction, lengths in FINGERS + [THUMB]:
            base = v(J["palm"]) + v(off) if name != "Thumb" else v(J["wrist"]) + v(off)
            pts = finger_chain(base, direction, lengths)
            start = palm if name != "Thumb" else wr
            radii = [0.016, 0.014, 0.011, 0.008] if name != "Thumb" else [0.018, 0.016, 0.012, 0.009]
            g.chain(start, [s(p) for p in pts], radii, steps=1)

        # Jambe : hanche, cuisse, genou, mollet, cheville, pied
        hp = g.chain(hips, [s(J["hip"])], [(0.11, 0.1)], steps=1)
        mid_thigh = s(v(J["hip"]).lerp(v(J["knee"]), 0.4))
        kn = g.chain(hp, [mid_thigh, s(J["knee"])], [(0.095, 0.09), (0.058, 0.06)])
        calf = s(v(J["knee"]).lerp(v(J["ankle"]), 0.35) + Vector((0.0, 0.03, 0.0)))
        an = g.chain(kn, [calf, s(J["ankle"])], [(0.062, 0.065), (0.036, 0.036)])
        ball = g.chain(an, [s(J["ball"])], [(0.045, 0.025)])
        g.chain(an, [s(J["heel"])], [0.022], steps=1)   # ergot
        for name, dx, direction, lengths in TOES:
            base = v(J["ball"]) + Vector((dx, 0.0, 0.0))
            d = v(direction).normalized()
            pts = [base + d * lengths[0], base + d * (lengths[0] + lengths[1])]
            g.chain(ball, [s(p) for p in pts], [0.016, 0.01], steps=1)

    # Queue
    tp = tail_points()
    radii = [max(0.012, 0.075 * (1.0 - i / TAIL_SEGMENTS) ** 1.2) for i in range(1, TAIL_SEGMENTS + 1)]
    g.chain(hips, tp[1:], radii, steps=2)
    return g


def build_body(coll, mats):
    g = build_body_graph()
    mesh = bpy.data.meshes.new("Demon_Body")
    mesh.from_pydata([tuple(p) for p in g.verts], g.edges, [])
    mesh.update()

    obj = bpy.data.objects.new("Demon_Body", mesh)
    coll.objects.link(obj)

    skin = obj.modifiers.new("Skin", "SKIN")
    skin.use_smooth_shade = True
    skin.branch_smoothing = 0.6
    layer = mesh.skin_vertices[0].data
    for i, r in enumerate(g.radii):
        layer[i].radius = r
        layer[i].use_root = (i == 0)

    sub = obj.modifiers.new("Subsurf", "SUBSURF")
    sub.levels = 2
    sub.render_levels = 2

    apply_all_modifiers(obj)
    sculpt_body(obj)
    obj.data.materials.append(mats["skin"])
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def apply_all_modifiers(obj):
    view_layer = bpy.context.view_layer
    for o in view_layer.objects:
        if o is not None:
            o.select_set(False)
    view_layer.objects.active = obj
    obj.select_set(True)
    for mod in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=mod.name)


def noise3(p, scale):
    from mathutils import noise
    return noise.noise(p * scale)


def sculpt_body(obj):
    """Details sculptes par code : cotes, vertebres, orbites, rides, nervures."""
    mesh = obj.data
    mesh.calc_normals_split() if hasattr(mesh, "calc_normals_split") else None
    normals = [vx.normal.copy() for vx in mesh.vertices]
    eyes = [v(J["eye"]), mirror(J["eye"])]

    for vx, n in zip(mesh.vertices, normals):
        p = vx.co.copy()
        d = 0.0

        # Cotes saillantes sur les flancs et le devant du torse
        # (limitees au tronc : pas sur les bras qui pendent a cote)
        if 1.34 < p.z < 1.68 and 0.03 < abs(p.x) < 0.135 and abs(p.y + 0.04) < 0.15:
            ribs = max(0.0, math.sin((p.z - 1.34) / 0.045 * math.pi * 2.0))
            fade = min(1.0, (p.z - 1.34) / 0.05, (1.68 - p.z) / 0.05, (0.135 - abs(p.x)) / 0.03)
            d += 0.016 * ribs ** 2 * fade

        # Ventre creuse
        if 1.18 < p.z < 1.38 and p.y < -0.03:
            d -= 0.018 * math.sin((p.z - 1.18) / 0.2 * math.pi)

        # Vertebres sur le dos
        if 1.15 < p.z < 1.85 and p.y > 0.0 and abs(p.x) < 0.035:
            bump = max(0.0, math.sin((p.z - 1.15) / 0.055 * math.pi * 2.0))
            d += 0.014 * bump * (1.0 - abs(p.x) / 0.035)

        # Orbites creusees + arcade sourciliere
        for e in eyes:
            dist = (p - e).length
            if dist < 0.045:
                d -= 0.02 * (1.0 - dist / 0.045)
            brow = (p - (e + Vector((0.0, 0.0, 0.03)))).length
            if brow < 0.04 and p.y < -0.2:
                d += 0.012 * (1.0 - brow / 0.04)

        # Peau ridee / tendue sur tout le corps
        d += 0.006 * noise3(p, 18.0) + 0.0025 * noise3(p, 55.0)

        vx.co = p + n * d
    mesh.update()


# ----------------------------------------------------------------------
# Pieces rigides : cornes, griffes, dents, epines, yeux
# ----------------------------------------------------------------------

def tube(bm, points, radii, segments=10, mat_index=0, cap=True):
    """Tube effile le long d'une polyline (dernier anneau pointu)."""
    rings = []
    for i, p in enumerate(points):
        if i < len(points) - 1:
            tangent = (points[i + 1] - p).normalized()
        else:
            tangent = (p - points[i - 1]).normalized()
        helper = Vector((0, 0, 1)) if abs(tangent.z) < 0.9 else Vector((1, 0, 0))
        a = tangent.cross(helper).normalized()
        b = tangent.cross(a).normalized()
        r = radii[i]
        if r <= 1e-5:
            rings.append([bm.verts.new(p)])
            continue
        ring = []
        for k in range(segments):
            ang = 2 * math.pi * k / segments
            ring.append(bm.verts.new(p + (a * math.cos(ang) + b * math.sin(ang)) * r))
        rings.append(ring)

    for r0, r1 in zip(rings, rings[1:]):
        if len(r1) == 1:
            for k in range(len(r0)):
                f = bm.faces.new((r0[k], r0[(k + 1) % len(r0)], r1[0]))
                f.material_index = mat_index
        else:
            for k in range(len(r0)):
                f = bm.faces.new((r0[k], r0[(k + 1) % len(r0)], r1[(k + 1) % len(r1)], r1[k]))
                f.material_index = mat_index
    if cap and len(rings[0]) > 2:
        f = bm.faces.new(list(reversed(rings[0])))
        f.material_index = mat_index


def horn_points(side):
    """Grande corne : monte, part en arriere puis s'enroule vers l'exterieur."""
    base = Vector((0.055 * side, -0.19, 2.13))
    pts, radii = [], []
    n = 22
    for i in range(n + 1):
        t = i / n
        ang = t * math.pi * 1.25
        x = base.x + side * (0.02 + 0.2 * t + 0.06 * math.sin(ang))
        y = base.y + 0.28 * math.sin(ang * 0.8) + 0.05 * t
        z = base.z + 0.22 * math.sin(ang * 0.55) - 0.1 * t * t
        pts.append(Vector((x, y, z)))
        ridge = 1.0 + 0.08 * math.sin(i * 2.4)
        radii.append(0.042 * (1.0 - t) ** 1.1 * ridge)
    radii[-1] = 0.0
    return pts, radii


def claw(base, direction, length, radius, curl_axis=Vector((1, 0, 0)), curl=0.6):
    d = direction.normalized()
    pts, radii = [], []
    n = 6
    for i in range(n + 1):
        t = i / n
        rot = Matrix.Rotation(curl * t, 3, curl_axis)
        pts.append(base + (rot @ d) * length * t)
        radii.append(radius * (1.0 - t))
    radii[-1] = 0.0
    return pts, radii


def rigid_object(name, coll, mat, bone, build):
    bm = bmesh.new()
    build(bm)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    coll.objects.link(obj)
    obj.data.materials.append(mat)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    group = obj.vertex_groups.new(name=bone)
    group.add(list(range(len(mesh.vertices))), 1.0, "REPLACE")
    return obj


def build_extras(coll, mats):
    parts = []

    for side, sfx in ((1, "L"), (-1, "R")):
        pts, radii = horn_points(side)
        parts.append(rigid_object("Horn." + sfx, coll, mats["horn"], "Head",
                                  lambda bm, p=pts, r=radii: tube(bm, p, r, 12)))

        # Griffes des doigts (sur la phalange distale)
        for name, off, direction, lengths in FINGERS + [THUMB]:
            base = v(J["palm"]) + v(off) if name != "Thumb" else v(J["wrist"]) + v(off)
            fp = finger_chain(base, direction, lengths)
            tip = fp[-1]
            d = (fp[-1] - fp[-2]).normalized()
            tip = Vector((tip.x * side, tip.y, tip.z))
            d = Vector((d.x * side, d.y, d.z))
            pts, radii = claw(tip - d * 0.01, d, 0.06, 0.009, curl_axis=Vector((1, 0, 0)), curl=-0.9)
            bone = "%sDistal.%s" % (name, sfx)
            parts.append(rigid_object("Claw_%s.%s" % (name, sfx), coll, mats["claw"], bone,
                                      lambda bm, p=pts, r=radii: tube(bm, p, r, 8)))

        # Griffes des orteils
        for name, dx, direction, lengths in TOES:
            base = v(J["ball"]) + Vector((dx, 0.0, 0.0))
            d = v(direction).normalized()
            tip = base + d * (lengths[0] + lengths[1])
            tip = Vector((tip.x * side, tip.y, tip.z))
            dd = Vector((d.x * side, d.y, d.z))
            pts, radii = claw(tip - dd * 0.005, dd, 0.05, 0.01, curl_axis=Vector((1, 0, 0)), curl=0.9)
            parts.append(rigid_object("Claw_%s.%s" % (name, sfx), coll, mats["claw"], "%s2.%s" % (name, sfx),
                                      lambda bm, p=pts, r=radii: tube(bm, p, r, 8)))

        # Oeil
        eye = Vector((J["eye"][0] * side, J["eye"][1] - 0.005, J["eye"][2]))
        def build_eye(bm, c=eye):
            bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=0.016,
                                      matrix=Matrix.Translation(c))
        parts.append(rigid_object("Eye." + sfx, coll, mats["eye"], "Eye." + sfx, build_eye))

    # Dents : machoire superieure (Head) et inferieure (Jaw)
    def teeth(bm, upper):
        for i in range(9):
            t = i / 8.0
            x = (t - 0.5) * 0.085
            y = -0.30 + 0.09 * (abs(t - 0.5) * 2) ** 2
            z = 1.985 if upper else 1.955
            base = Vector((x, y, z))
            length = 0.035 if i in (1, 7) else 0.022
            d = Vector((0, -0.15, -1.0 if upper else 1.0))
            pts, radii = claw(base, d, length, 0.006, curl=0.0)
            tube(bm, pts, radii, 6)
    parts.append(rigid_object("Teeth_Upper", coll, mats["teeth"], "Head", lambda bm: teeth(bm, True)))
    parts.append(rigid_object("Teeth_Lower", coll, mats["teeth"], "Jaw", lambda bm: teeth(bm, False)))

    # Epines le long de la colonne
    spine_bones = [("Spine", 1.30), ("Chest", 1.48), ("Chest", 1.56), ("UpperChest", 1.66), ("UpperChest", 1.74)]
    for i, (bone, z) in enumerate(spine_bones):
        y = 0.11 + 0.02 * math.sin(i)
        base = Vector((0.0, y - 0.03, z))
        pts, radii = claw(base, Vector((0, 1.0, 0.5)), 0.07 + 0.015 * (i % 2), 0.018, curl=0.5)
        parts.append(rigid_object("Spike_%02d" % i, coll, mats["horn"], bone,
                                  lambda bm, p=pts, r=radii: tube(bm, p, r, 8)))
    return parts


# ----------------------------------------------------------------------
# Squelette
# ----------------------------------------------------------------------

def build_armature(coll):
    arm_data = bpy.data.armatures.new("Demon_Rig")
    arm_data.display_type = "OCTAHEDRAL"
    arm = bpy.data.objects.new("Demon_Rig", arm_data)
    coll.objects.link(arm)
    arm.show_in_front = True

    view_layer = bpy.context.view_layer
    for o in view_layer.objects:
        if o is not None:
            o.select_set(False)
    view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones

    def bone(name, head, tail, parent=None, connect=False, deform=True, roll=0.0):
        b = eb.new(name)
        b.head = Vector(head)
        b.tail = Vector(tail)
        b.roll = roll
        if parent:
            b.parent = eb[parent]
            b.use_connect = connect
        b.use_deform = deform
        return b

    # Axe
    bone("Hips", J["hips"], J["spine"])
    bone("Spine", J["spine"], J["chest"], "Hips", True)
    bone("Chest", J["chest"], J["upperchest"], "Spine", True)
    bone("UpperChest", J["upperchest"], J["neck"], "Chest", True)
    bone("Neck", J["neck"], J["head"], "UpperChest", True)
    bone("Head", J["head"], J["headtop"], "Neck", True)
    bone("Jaw", J["jaw"], J["jawtip"], "Head")
    bone("Eye.L", J["eye"], v(J["eye"]) + Vector((0, -0.04, 0)), "Head")

    # Bras gauche
    bone("Shoulder.L", J["clavicle"], J["shoulder"], "UpperChest")
    bone("UpperArm.L", J["shoulder"], J["elbow"], "Shoulder.L", True)
    bone("LowerArm.L", J["elbow"], J["wrist"], "UpperArm.L", True)
    bone("Hand.L", J["wrist"], J["palm"], "LowerArm.L", True)
    for name, off, direction, lengths in FINGERS + [THUMB]:
        base = v(J["palm"]) + v(off) if name != "Thumb" else v(J["wrist"]) + v(off)
        pts = finger_chain(base, direction, lengths)
        parent = "Hand.L"
        for k, part in enumerate(("Proximal", "Intermediate", "Distal")):
            bname = "%s%s.L" % (name, part)
            bone(bname, pts[k], pts[k + 1], parent, k > 0)
            parent = bname

    # Jambe gauche
    bone("UpperLeg.L", J["hip"], J["knee"], "Hips")
    bone("LowerLeg.L", J["knee"], J["ankle"], "UpperLeg.L", True)
    bone("Foot.L", J["ankle"], J["ball"], "LowerLeg.L", True)
    bone("Toes.L", J["ball"], v(J["ball"]) + Vector((0, -0.05, -0.012)), "Foot.L", True)
    bone("Heel.L", J["ankle"], J["heel"], "Foot.L")
    for name, dx, direction, lengths in TOES:
        base = v(J["ball"]) + Vector((dx, 0.0, 0.0))
        d = v(direction).normalized()
        p1 = base + d * lengths[0]
        p2 = p1 + d * lengths[1]
        bone(name + "1.L", base, p1, "Toes.L")
        bone(name + "2.L", p1, p2, name + "1.L", True)

    # Controleurs IK (non deformants, non exportes)
    bone("IK_Foot.L", J["ankle"], v(J["ankle"]) + Vector((0, -0.15, 0)), deform=False)
    bone("Pole_Knee.L", v(J["knee"]) + Vector((0, -0.6, 0)), v(J["knee"]) + Vector((0, -0.7, 0)), deform=False)
    bone("IK_Hand.L", J["wrist"], v(J["wrist"]) + Vector((0, -0.12, 0)), deform=False)
    bone("Pole_Elbow.L", v(J["elbow"]) + Vector((0, 0.55, 0)), v(J["elbow"]) + Vector((0, 0.65, 0)), deform=False)

    # Queue
    tp = tail_points()
    parent = "Hips"
    for i in range(TAIL_SEGMENTS):
        name = "Tail_%02d" % (i + 1)
        bone(name, tp[i], tp[i + 1], parent, i > 0)
        parent = name

    # Roulis coherent puis miroir gauche -> droite
    for b in eb:
        b.select = b.select_head = b.select_tail = True
    bpy.ops.armature.calculate_roll(type="GLOBAL_NEG_Y")
    for b in eb:
        b.select = b.select_head = b.select_tail = False
    for b in eb:
        if b.name.endswith(".L"):
            b.select = b.select_head = b.select_tail = True
    bpy.ops.armature.symmetrize(direction="POSITIVE_X")
    bpy.ops.object.mode_set(mode="OBJECT")

    setup_ik(arm)
    arm_data.collections.new("Deform")
    ctrl = arm_data.collections.new("Controls")
    for b in arm_data.bones:
        if not b.use_deform:
            ctrl.assign(b)
            b.color.palette = "THEME09"
        else:
            arm_data.collections["Deform"].assign(b)
    return arm


def setup_ik(arm):
    """IK jambes et bras, angle de pole choisi pour ne pas bouger la pose de repos."""
    for sfx in ("L", "R"):
        for lower, target, pole in (("LowerLeg", "IK_Foot", "Pole_Knee"), ("LowerArm", "IK_Hand", "Pole_Elbow")):
            pb = arm.pose.bones["%s.%s" % (lower, sfx)]
            con = pb.constraints.new("IK")
            con.target = arm
            con.subtarget = "%s.%s" % (target, sfx)
            con.pole_target = arm
            con.pole_subtarget = "%s.%s" % (pole, sfx)
            con.chain_count = 2
            best, best_err = 0.0, 1e9
            upper = arm.pose.bones["%s.%s" % (lower.replace("Lower", "Upper"), sfx)]
            for deg in range(-180, 180, 5):
                con.pole_angle = math.radians(deg)
                bpy.context.view_layer.update()
                err = sum(abs(a - b) for ra, rb in zip(pb.matrix, arm.data.bones[pb.name].matrix_local) for a, b in zip(ra, rb))
                err += sum(abs(a - b) for ra, rb in zip(upper.matrix, arm.data.bones[upper.name].matrix_local) for a, b in zip(ra, rb))
                if err < best_err:
                    best, best_err = deg, err
            con.pole_angle = math.radians(best)
    bpy.context.view_layer.update()


# ----------------------------------------------------------------------
# Assemblage
# ----------------------------------------------------------------------

def build_demon():
    scene = get_scene()
    clear_scene(scene)
    coll = bpy.data.collections.new("Demon")
    scene.collection.children.link(coll)
    mats = materials()

    body = build_body(coll, mats)
    arm = build_armature(coll)

    # Peau pesee automatiquement (chaleur des os) sur les os deformants
    view_layer = bpy.context.view_layer
    for o in view_layer.objects:
        if o is not None:
            o.select_set(False)
    body.select_set(True)
    arm.select_set(True)
    view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")

    # Pieces rigides : un os chacune, puis jointes au corps
    extras = build_extras(coll, mats)
    for o in view_layer.objects:
        if o is not None:
            o.select_set(False)
    for e in extras:
        e.select_set(True)
    body.select_set(True)
    view_layer.objects.active = body
    bpy.ops.object.join()
    body.name = "Demon"
    body.data.name = "Demon"

    # Nettoyage des groupes vides / non deformants
    deform = {b.name for b in arm.data.bones if b.use_deform}
    for g in list(body.vertex_groups):
        if g.name not in deform:
            body.vertex_groups.remove(g)

    add_uvs(body)
    frame_view(scene)
    return {"verts": len(body.data.vertices), "faces": len(body.data.polygons),
            "bones": len(arm.data.bones), "deform": len(deform)}


def add_uvs(obj):
    view_layer = bpy.context.view_layer
    for o in view_layer.objects:
        if o is not None:
            o.select_set(False)
    view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.003)
    bpy.ops.object.mode_set(mode="OBJECT")


def frame_view(scene):
    cam = bpy.data.objects.get("Demon_Cam")
    if cam is None:
        cam = bpy.data.objects.new("Demon_Cam", bpy.data.cameras.new("Demon_Cam"))
        scene.collection.objects.link(cam)
    cam.location = (2.3, -4.2, 1.5)
    direction = Vector((0, 0, 1.15)) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 50
    scene.camera = cam
    if scene.world is None:
        scene.world = bpy.data.worlds.get("Demon_World") or bpy.data.worlds.new("Demon_World")
    scene.world.color = (0.25, 0.27, 0.3)
    if bpy.data.objects.get("Demon_Sun") is None:
        sun = bpy.data.objects.new("Demon_Sun", bpy.data.lights.new("Demon_Sun", "SUN"))
        sun.data.energy = 3.0
        sun.rotation_euler = (math.radians(50), 0, math.radians(35))
        scene.collection.objects.link(sun)


def export_demon(path=FBX_PATH):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    body = bpy.data.objects["Demon"]
    arm = bpy.data.objects["Demon_Rig"]
    view_layer = bpy.context.view_layer
    for o in view_layer.objects:
        if o is not None:
            o.select_set(False)
    body.select_set(True)
    arm.select_set(True)
    view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH", "ARMATURE"},
        axis_forward="-Z",
        axis_up="Y",
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=False,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        use_armature_deform_only=True,
        armature_nodetype="NULL",
        bake_anim=False,
    )
    return path
