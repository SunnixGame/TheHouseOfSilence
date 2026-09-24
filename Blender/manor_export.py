# -*- coding: utf-8 -*-
"""
manor_export.py — Etape 4 : export FBX du manoir vers Unity 6.

Usage (dans Blender, apres manor_builder.py) :
    exec(open(r"...\\manor_export.py", encoding="utf-8").read())
    result = export_manor()        # -> Assets/_Game/Art/Manor/Manor.fbx

Ce que fait l'export :
  - reconstruit une hierarchie d'Empties calquee sur les collections (MANOR_ROOT /
    GROUND_FLOOR / GF_Walls / ...) et y parente les objets : Unity retrouve
    l'arborescence demandee dans le prefab importe ;
  - exclut le terrain de reference (hos_export = False) et les empties d'anomalies ;
  - exporte les proprietes personnalisees hos_* (type, niveau, piece, portes : gond,
    sens d'ouverture...) : le postprocesseur Unity les lit pour poser colliders,
    scripts de porte et volumes de pieces ;
  - axes -Z avant / Y haut, unites metres, modificateurs appliques (toit).
"""

import os

import bpy

FBX_PATH = os.path.normpath(os.path.join(os.path.dirname(bpy.data.filepath) or ".", "..", "Assets", "_Game", "Art", "Manor", "Manor.fbx"))


def collection_path(coll, root_name="MANOR_ROOT"):
    """Chemin 'MANOR_ROOT/GROUND_FLOOR/GF_Walls' d'une collection."""
    parents = {c: p for p in bpy.data.collections for c in p.children}
    path = [coll.name]
    while coll in parents and coll.name != root_name:
        coll = parents[coll]
        path.insert(0, coll.name)
    return "/".join(path)


def build_hierarchy_empties(root_name="MANOR_ROOT"):
    """Un Empty par collection, objets parentes dessous. Idempotent."""
    root_coll = bpy.data.collections[root_name]
    empties = {}

    def ensure_empty(coll, parent_empty):
        name = "H_" + coll.name if coll.name != root_name else root_name
        obj = bpy.data.objects.get(name)
        if obj is None:
            obj = bpy.data.objects.new(name, None)
            obj.empty_display_type = "PLAIN_AXES"
            obj.empty_display_size = 0.2
            coll.objects.link(obj)
        obj["hos_type"] = "group"
        obj["hos_group"] = coll.name
        if parent_empty is not None and obj.parent is not parent_empty:
            obj.parent = parent_empty
        empties[coll] = obj
        for child in coll.children:
            ensure_empty(child, obj)

    ensure_empty(root_coll, None)
    count = 0
    for coll, empty in empties.items():
        for obj in coll.objects:
            if obj is empty or obj.get("hos_type") == "group":
                continue
            if obj.parent is not empty:
                world = obj.matrix_world.copy()
                obj.parent = empty
                obj.matrix_world = world
                count += 1
    return count


def add_box_uvs(mesh):
    """UV par projection boite : chaque face est projetee selon l'axe dominant de sa normale,
    en unites metres (les materiaux Unity tuilent donc en espace monde)."""
    if mesh.uv_layers:
        return False
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
    return True


def export_manor(path=None, include_issues=False):
    path = path or FBX_PATH
    os.makedirs(os.path.dirname(path), exist_ok=True)
    reparented = build_hierarchy_empties()
    uv_count = sum(1 for m in bpy.data.meshes if add_box_uvs(m))

    # selection : tout MANOR_ROOT sauf exclusions
    excluded = []
    for obj in bpy.data.objects:
        obj.select_set(False)
    root_coll = bpy.data.collections["MANOR_ROOT"]
    stack = [root_coll]
    selected = 0
    while stack:
        coll = stack.pop()
        stack.extend(coll.children)
        for obj in coll.objects:
            if obj.get("hos_export") is False or (not include_issues and coll.name.endswith("Issues")):
                excluded.append(obj.name)
                continue
            if obj.hide_viewport:
                obj.hide_viewport = False
            obj.select_set(True)
            selected += 1
    # les empties de hierarchie sont dans les collections : deja selectionnes

    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH", "EMPTY"},
        axis_forward="-Z",
        axis_up="Y",
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=False,      # la conversion d'axes est faite par Unity (bakeAxisConversion)
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
    for obj in bpy.data.objects:
        obj.select_set(False)
    size = os.path.getsize(path) if os.path.exists(path) else 0
    return dict(fbx=path, objects=selected, excluded=excluded, reparented=reparented, size_mb=round(size / 1e6, 1), uv_added=uv_count)
