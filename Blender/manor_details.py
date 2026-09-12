# -*- coding: utf-8 -*-
"""
manor_details.py — Etape 2 : architecture detaillee du manoir.

Execute par manor_builder.py apres le blockout, dans le meme espace de noms
(helpers, niveaux, constantes). Tout ce qui est genere ici est decoratif ou
secondaire (garde-corps, colonnes, voutes, encadrements, plinthes, corniches,
lambris, poutres, cheminees, lucarnes, bandeaux) et vit dans des objets separes :
le blockout reste la structure de reference.

Regles :
  - aucun booleen : tout est assemblage de boites et de prismes ;
  - un objet fusionne par type et par niveau pour les petits elements repetitifs
    (plinthes, corniches, encadrements...), un objet par element notable
    (cheminee, garde-corps, colonne, lucarne) ;
  - rien ne doit reduire un passage sous 1.2 m ni une hauteur libre sous 1.9 m.
"""

import math

DETAIL_COLL = "%s/Decorative_Architecture"

# Pieces qui recoivent des lambris (bas de mur en bois a 0.9 m)
WAINSCOT_ROOMS = {
    "GF": {"Hall", "Salon", "Bibliotheque", "Bureau", "Reception", "SalleAManger", "GrandCouloir", "Musique"},
    "F1": {"LongCouloir", "ChambrePrincipale", "BalconS", "BalconW", "BalconE", "Pont", "GalerieW", "GalerieE", "Chambre2"},
}
# Pieces sans corniche (service, technique)
NO_CORNICE = {"Cellier", "Buanderie", "EscService", "EscSousSol", "Cuisine", "SalleDomestiques", "CouloirService",
              "PuitsSecret", "PassageSecret", "Placard", "Debarras", "CouloirServiceF1", "ChambreDomestique1",
              "ChambreDomestique2", "ChambreDomestique3", "SdB_RDC"}
# Pieces a poutres apparentes
BEAM_ROOMS = {
    "GF": {"Cuisine", "SalleDomestiques", "Buanderie", "Cellier", "Office"},
    "B1": {"Atelier", "LocalTechnique", "Chaufferie", "Stockage", "Reserve", "Remise"},
    "F2": None,   # None = toutes les pieces avec plancher
}
# Foyers : (niveau, piece, cote du mur, position t le long du mur)  — les souches CHIMNEYS sont a l'aplomb
FIREPLACES = [
    ("GF", "Salon", "N", 0.5), ("GF", "Bibliotheque", "S", 0.5),
    ("GF", "Bibliotheque", "E", 0.5), ("GF", "Bureau", "W", 0.5),
    ("GF", "Reception", "N", 0.15), ("GF", "SalleAManger", "S", 0.25),
    ("GF", "Cuisine", "N", 1.0 / 3),
    ("F1", "Chambre2", "N", 7.0 / 12), ("F1", "ChambreEnfant", "S", 7.0 / 12),
    ("F1", "Chambre2", "E", 0.5), ("F1", "Chambre3", "W", 0.5),
    ("F1", "ChambreEnfant", "E", 0.5), ("F1", "BureauPrive", "W", 0.5),
    ("F1", "ChambrePrincipale", "N", 0.25), ("F1", "ChambreAmis", "N", 1.0 / 3),
    ("F2", "SalleDeJeux", "W", 0.5), ("F2", "ChambreAbandonnee2", "E", 0.325),
]
PILLARS = {   # (x, y, demi-cote) par piece du sous-sol
    "CaveAVin": [(4, 17.5, 0.25), (8, 17.5, 0.25)],
    "SalleRituelle": [(4, 4.7, 0.3), (8, 4.7, 0.3), (4, 9.3, 0.3), (8, 9.3, 0.3)],
    "Archives": [(44, 11.5, 0.25), (48, 11.5, 0.25)],
    "Atelier": [(18, 8, 0.25), (18, 15, 0.25)],
}
DORMERS_S = [8, 16, 36, 44]
DORMERS_N = [10, 26, 42]


# =============================================================================
#  ACCUMULATEUR DE GEOMETRIE
# =============================================================================

class MeshAcc:
    """Accumule boites et prismes pour produire un seul objet."""

    def __init__(self):
        self.verts, self.faces = [], []

    def box(self, x0, x1, y0, y1, z0, z1):
        if x1 - x0 < 1e-5 or y1 - y0 < 1e-5 or z1 - z0 < 1e-5:
            return
        v, f = box_geometry(x0, x1, y0, y1, z0, z1, offset=len(self.verts))
        self.verts.extend(v)
        self.faces.extend(f)

    def poly(self, verts, faces):
        off = len(self.verts)
        self.verts.extend(verts)
        self.faces.extend([tuple(i + off for i in f) for f in faces])

    def bar(self, p0, p1, z0, z1, width, height):
        """Barre inclinee de (x0,y0,z0) a (x1,y1,z1), section width (horizontale) x height."""
        dx, dy = p1[0] - p0[0], p1[1] - p0[1]
        length = math.hypot(dx, dy)
        if length < 1e-6:
            return
        nx, ny = -dy / length * width / 2, dx / length * width / 2
        quad = [(p0[0] + nx, p0[1] + ny, z0), (p0[0] - nx, p0[1] - ny, z0),
                (p1[0] - nx, p1[1] - ny, z1), (p1[0] + nx, p1[1] + ny, z1)]
        verts = quad + [(x, y, z + height) for (x, y, z) in quad]
        faces = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
        self.poly(verts, faces)

    def finish(self, name, coll, mat, props=None):
        if not self.faces:
            return None
        return make_poly_object(name, self.verts, self.faces, coll, mat, props=props)


def side_frame(room, side, level):
    """Pour un cote de piece : (axe, ligne, a, b, signe vers l'interieur, epaisseur du mur)."""
    axis, line, a, b = room.edge(side)
    sign = +1 if side in ("S", "W") else -1
    thk = level.ext_thk if side in exterior_sides(level, room) else level.int_thk
    return axis, line, a, b, sign, thk


def along_box(acc, axis, line, u0, u1, d0, d1, z0, z1):
    """Boite exprimee le long d'un mur : u = abscisse le long du mur, d = distance signee a la ligne."""
    lo, hi = min(d0, d1), max(d0, d1)
    if axis == "x":
        acc.box(u0, u1, line + lo, line + hi, z0, z1)
    else:
        acc.box(line + lo, line + hi, u0, u1, z0, z1)


def openings_on(level, axis, line, a, b, kinds=None):
    out = []
    for op in level.openings:
        if op.get("skip") or op["axis"] != axis or abs(op["line"] - line) > 1e-6:
            continue
        if kinds is not None and op["kind"] not in kinds:
            continue
        lo, hi = op["center"] - op["width"] / 2, op["center"] + op["width"] / 2
        if hi > a + 1e-6 and lo < b - 1e-6:
            out.append((lo, hi, op))
    return sorted(out)


def wall_intervals(level, room, side):
    """Intervalles de ce cote de piece qui portent reellement un mur (pas d'arete ouverte de meme zone)."""
    axis, line, a, b = room.edge(side)
    ivs = []
    for p in level.pieces:
        if p["axis"] != axis or abs(p["line"] - line) > 1e-6:
            continue
        if p["neg"] is not room and p["pos"] is not room:
            continue
        ov = rect_overlap_1d(a, b, p["a"], p["b"])
        if ov:
            ivs.append(ov)
    ivs.sort()
    merged = []
    for iv in ivs:
        if merged and abs(merged[-1][1] - iv[0]) < 1e-6:
            merged[-1] = (merged[-1][0], iv[1])
        else:
            merged.append(iv)
    return merged


# =============================================================================
#  GARDE-CORPS
# =============================================================================

def railing(acc, p0, p1, z0, z1, height=1.0, spacing=0.14, posts=(True, True), bal=0.035):
    L = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
    if L < 0.05:
        return
    n = max(1, int(round(L / spacing)))
    for i in range(1, n):
        t = i / n
        x, y = p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t
        z = z0 + (z1 - z0) * t
        acc.box(x - bal / 2, x + bal / 2, y - bal / 2, y + bal / 2, z + 0.02, z + height - 0.08)
    for flag, (x, y, z) in zip(posts, ((p0[0], p0[1], z0), (p1[0], p1[1], z1))):
        if flag:
            acc.box(x - 0.06, x + 0.06, y - 0.06, y + 0.06, z, z + height + 0.08)
            acc.box(x - 0.08, x + 0.08, y - 0.08, y + 0.08, z + height + 0.08, z + height + 0.14)
    acc.bar(p0, p1, z0 + height - 0.08, z1 + height - 0.08, 0.09, 0.08)   # main courante
    acc.bar(p0, p1, z0 + 0.06, z1 + 0.06, 0.05, 0.04)                     # lisse basse


def build_railings(levels):
    B1, GF, F1, F2, TUN, SHED = levels
    made = []

    # --- Balcon et pont du 1er, galeries autour de la cage du grand escalier
    segs = [
        ("F1_Railing_BalconS", (22.5, 4.5), (29.5, 4.5)),
        ("F1_Railing_BalconW", (22.5, 4.5), (22.5, 11.0)),
        ("F1_Railing_BalconE", (29.5, 4.5), (29.5, 11.0)),
        ("F1_Railing_PontSud", (22.5, 11.0), (29.5, 11.0)),
        ("F1_Railing_PontNord", (24.0, 13.0), (28.0, 13.0)),
        ("F1_Railing_GalerieW", (22.0, 13.0), (22.0, 19.0)),
        ("F1_Railing_GalerieE", (30.0, 13.0), (30.0, 19.0)),
    ]
    for name, p0, p1 in segs:
        acc = MeshAcc()
        railing(acc, p0, p1, F1.z, F1.z, height=1.0)
        made.append(acc.finish(name, DETAIL_COLL % "FIRST_FLOOR", "M_Wood_Dark", {"hos_type": "railing", "hos_level": "F1"}))

    # --- Grand escalier imperial : volee centrale, volees retour, bords du palier
    z0, z_mid, z_top = GF.z, (GF.z + F1.z) / 2, F1.z
    acc = MeshAcc()
    railing(acc, (24.25, 13.0), (24.25, 16.5), z0, z_mid, posts=(True, False))
    railing(acc, (27.75, 13.0), (27.75, 16.5), z0, z_mid, posts=(True, False))
    railing(acc, (23.75, 16.5), (23.75, 13.0), z_mid, z_top, posts=(False, True))
    railing(acc, (28.25, 16.5), (28.25, 13.0), z_mid, z_top, posts=(False, True))
    railing(acc, (22.25, 16.5), (22.25, 13.0), z_mid, z_top, posts=(True, True))
    railing(acc, (29.75, 16.5), (29.75, 13.0), z_mid, z_top, posts=(True, True))
    railing(acc, (22.1, 16.5), (22.1, 18.85), z_mid, z_mid, posts=(False, True))
    railing(acc, (29.9, 16.5), (29.9, 18.85), z_mid, z_mid, posts=(False, True))
    made.append(acc.finish("GF_Railing_GrandEscalier", "GROUND_FLOOR/Stairs", "M_Wood_Dark", {"hos_type": "railing", "hos_level": "GF"}))

    # --- Escaliers en U : lisse centrale entre les deux volees, garde-corps de la tremie au dernier niveau
    def u_stair_rails(level_bottom, room_name, z_levels, name, coll, mat):
        r = level_bottom.room(room_name)
        W = r.x1 - r.x0
        lane_w = (W - 0.1) / 2
        xin1, xin2 = r.x0 + lane_w - 0.06, r.x1 - lane_w + 0.06
        yf0, yf1 = r.y0 + ARRIVAL, r.y1 - LANDING
        acc = MeshAcc()
        for za, zb in zip(z_levels[:-1], z_levels[1:]):
            zm = (za + zb) / 2
            railing(acc, (xin1, yf0), (xin1, yf1), za, zm, height=0.95, posts=(True, True))
            railing(acc, (xin2, yf1), (xin2, yf0), zm, zb, height=0.95, posts=(True, True))
            railing(acc, (xin1, yf1 + 0.05), (xin2, yf1 + 0.05), zm, zm, height=0.95, posts=(False, False))
        # dernier niveau : seule la bande d'arrivee existe ; on ferme son bord nord au-dessus
        # du vide de la volee 1. (Pas de lisse horizontale au-dessus de la volee ni du palier :
        # elle flotterait a hauteur de tete de qui monte.)
        z_last = z_levels[-1]
        railing(acc, (r.x0 + 0.06, yf0), (xin1 + 0.06, yf0), z_last, z_last, height=0.95, posts=(True, True))
        made.append(acc.finish(name, coll, mat, {"hos_type": "railing"}))

    u_stair_rails(B1, "EscService", [B1.z, GF.z, F1.z, F2.z], "GF_Railing_Service", "GROUND_FLOOR/Stairs", "M_Wood_Dark")
    u_stair_rails(B1, "EscSousSol", [B1.z, GF.z], "B1_Railing_SousSol", "BASEMENT/Stairs", "M_Metal_Rust")
    u_stair_rails(TUN, "EscSecondaire", [TUN.z, SHED.z], "TUN_Railing_Secondaire", "BASEMENT/Stairs", "M_Metal_Rust")

    # --- Balustrades exterieures en pierre : terrasse est, toit du porche
    acc = MeshAcc()
    for p0, p1 in (((52.3, 0.1), (57.9, 0.1)), ((52.3, 9.9), (57.9, 9.9)), ((57.9, 0.1), (57.9, 3.0)), ((57.9, 7.0), (57.9, 9.9))):
        railing(acc, p0, p1, 0.0, 0.0, height=0.9, spacing=0.22, bal=0.09)
    made.append(acc.finish("EXT_Balustrade_Terrace", "EXTERIOR/Decorations", "M_Stone_Old", {"hos_type": "railing"}))
    acc = MeshAcc()
    zp = GF.ceiling_z + 0.4
    for p0, p1 in (((21.1, -4.4), (30.9, -4.4)), ((21.1, -4.4), (21.1, -0.1)), ((30.9, -4.4), (30.9, -0.1))):
        railing(acc, p0, p1, zp, zp, height=0.9, spacing=0.22, bal=0.09)
    made.append(acc.finish("EXT_Balustrade_Porch", "EXTERIOR/Decorations", "M_Stone_Old", {"hos_type": "railing"}))
    return "Garde-corps : %d objets" % len([m for m in made if m])


# =============================================================================
#  COLONNES, PILIERS, VOUTES
# =============================================================================

def arch(acc, p0, p1, z_spring, z_apex, depth=0.35, thick=0.3, segs=8):
    pts = []
    for k in range(segs + 1):
        t = k / segs
        pts.append((p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t, z_spring + (z_apex - z_spring) * math.sin(math.pi * t)))
    for a, b in zip(pts[:-1], pts[1:]):
        acc.bar((a[0], a[1]), (b[0], b[1]), a[2], b[2], depth, thick)


def build_columns_and_vaults(levels):
    B1, GF, F1, F2, TUN, SHED = levels
    count = 0
    # Hall : quatre colonnes sous les angles du balcon
    for i, (cx, cy) in enumerate([(22.5, 4.5), (29.5, 4.5), (22.5, 11.0), (29.5, 11.0)]):
        obj = build_cylinder(cx, cy, 0.28, GF.z + 0.12, GF.ceiling_z - 0.15, "GF_Column_Hall_%d" % (i + 1), "M_Stone_Old",
                             DETAIL_COLL % "GROUND_FLOOR", segments=20)
        obj["hos_type"] = "column"
        acc = MeshAcc()
        acc.box(cx - 0.42, cx + 0.42, cy - 0.42, cy + 0.42, GF.z, GF.z + 0.12)
        acc.box(cx - 0.45, cx + 0.45, cy - 0.45, cy + 0.45, GF.ceiling_z - 0.15, GF.ceiling_z)
        acc.finish("GF_Column_Hall_%d_Caps" % (i + 1), DETAIL_COLL % "GROUND_FLOOR", "M_Stone_Old", {"hos_type": "column"})
        count += 1

    # Sous-sol : piliers massifs et nervures en arc (spring 1.9 m, cle 2.7 m)
    zs, za = B1.z + 2.05, B1.z + 2.75
    for room_name, plist in PILLARS.items():
        for i, (px, py, hs) in enumerate(plist):
            acc = MeshAcc()
            acc.box(px - hs, px + hs, py - hs, py + hs, B1.z, B1.ceiling_z)
            acc.box(px - hs - 0.08, px + hs + 0.08, py - hs - 0.08, py + hs + 0.08, B1.z + 1.9, B1.z + 2.05)
            acc.finish("B1_Pillar_%s_%d" % (room_name, i + 1), DETAIL_COLL % "BASEMENT", "M_Stone_Old", {"hos_type": "column"})
            count += 1

    ribs = MeshAcc()
    # Cave a vin : nef le long de y = 17.5 et arcs transversaux aux piliers
    for a, b in (((0.25, 17.5), (4, 17.5)), ((4, 17.5), (8, 17.5)), ((8, 17.5), (11.85, 17.5))):
        arch(ribs, a, b, zs, za)
    for x in (4, 8):
        arch(ribs, (x, 14.15), (x, 17.5), zs, za)
        arch(ribs, (x, 17.5), (x, 20.85), zs, za)
    # Salle rituelle : trame 3 x 3 travees
    for y in (4.7, 9.3):
        for a, b in (((0.25, y), (4, y)), ((4, y), (8, y)), ((8, y), (11.85, y))):
            arch(ribs, a, b, zs, za)
    for x in (4, 8):
        for a, b in (((x, 0.25), (x, 4.7)), ((x, 4.7), (x, 9.3)), ((x, 9.3), (x, 13.85))):
            arch(ribs, a, b, zs, za)
    # Archives et atelier
    for a, b in (((40.15, 11.5), (44, 11.5)), ((44, 11.5), (48, 11.5)), ((48, 11.5), (51.75, 11.5))):
        arch(ribs, a, b, zs, za)
    for x in (44, 48):
        arch(ribs, (x, 2.15), (x, 11.5), zs, za)
        arch(ribs, (x, 11.5), (x, 20.85), zs, za)
    for y in (8, 15):
        arch(ribs, (12.15, y), (18, y), zs, za)
        arch(ribs, (18, y), (23.85, y), zs, za)
    arch(ribs, (18, 2.15), (18, 8), zs, za)
    arch(ribs, (18, 8), (18, 15), zs, za)
    arch(ribs, (18, 15), (18, 20.85), zs, za)
    # Couloirs : arcs doubleaux tous les 6 m
    for x in range(6, 49, 6):
        arch(ribs, (x, 21.15), (x, 23.85), zs, za, depth=0.4)
    for x in range(18, 49, 6):
        arch(ribs, (x, 0.25), (x, 1.85), zs + 0.2, za, depth=0.4)
    ribs.finish("B1_Vault_Ribs", DETAIL_COLL % "BASEMENT", "M_Stone_Old", {"hos_type": "vault"})

    # Tunnel : cintres tous les 4 m
    tz_s, tz_a = TUN.z + 1.9, TUN.z + 2.32
    tun = MeshAcc()
    for x in range(54, 73, 4):
        arch(tun, (x, 26.15), (x, 27.85), tz_s, tz_a, depth=0.4, thick=0.25)
    for x in range(78, 89, 4):
        arch(tun, (x, 38.15), (x, 39.85), tz_s, tz_a, depth=0.4, thick=0.25)
    for y in (30, 34):
        arch(tun, (74.15, y), (75.85, y), tz_s, tz_a, depth=0.4, thick=0.25)
    tun.finish("TUN_Vault_Ribs", "BASEMENT/Tunnels", "M_Stone_Old", {"hos_type": "vault"})
    return "Colonnes / piliers : %d, nervures de voute generees" % count


# =============================================================================
#  ENCADREMENTS DE PORTES ET FENETRES
# =============================================================================

OUTWARD = {"S": -1, "N": +1, "W": -1, "E": +1}


def build_frames(level, prefix):
    doors = MeshAcc()
    metal = MeshAcc()
    windows = MeshAcc()
    stone = MeshAcc()
    f = 0.08   # largeur des chambranles
    for op in level.openings:
        if op.get("skip") or "thk" not in op:
            continue
        lo, hi = op["center"] - op["width"] / 2, op["center"] + op["width"] / 2
        thk = op["thk"]
        d0, d1 = -thk / 2 - 0.02, thk / 2 + 0.02
        if op["kind"] == "window":
            sill, top = op["bottom"], op["top"]
            wd0, wd1 = -0.06, 0.06   # chassis centre sur la vitre
            along_box(windows, op["axis"], op["line"], lo - 0.07, lo, wd0, wd1, sill - 0.07, top + 0.07)
            along_box(windows, op["axis"], op["line"], hi, hi + 0.07, wd0, wd1, sill - 0.07, top + 0.07)
            along_box(windows, op["axis"], op["line"], lo, hi, wd0, wd1, sill - 0.07, sill)
            along_box(windows, op["axis"], op["line"], lo, hi, wd0, wd1, top, top + 0.07)
            along_box(windows, op["axis"], op["line"], op["center"] - 0.03, op["center"] + 0.03, wd0, wd1, sill, top)  # meneau
            if top - sill > 1.5:
                zt = sill + (top - sill) * 0.62
                along_box(windows, op["axis"], op["line"], lo, hi, wd0, wd1, zt - 0.03, zt + 0.03)                # traverse
            # appui et linteau en pierre a l'exterieur
            out = OUTWARD[op["side"]]
            face = out * thk / 2
            along_box(stone, op["axis"], op["line"], lo - 0.12, hi + 0.12, face, face + out * 0.12, sill - 0.08, sill + 0.02)
            along_box(stone, op["axis"], op["line"], lo - 0.12, hi + 0.12, face, face + out * 0.08, top + 0.07, top + 0.32)
            continue
        if op["kind"] == "breakable":
            continue
        acc = metal if op["kind"] in ("bars", "metal") else doors
        z0, z1 = level.z, op["top"]
        along_box(acc, op["axis"], op["line"], lo - f, lo, d0, d1, z0, z1 + f)
        along_box(acc, op["axis"], op["line"], hi, hi + f, d0, d1, z0, z1 + f)
        along_box(acc, op["axis"], op["line"], lo - f, hi + f, d0, d1, z1, z1 + f)
        if op["kind"] in ("double", "entrance", "opening") and level.key in ("GF", "F1"):
            # fronton / corniche de porte
            along_box(acc, op["axis"], op["line"], lo - f - 0.06, hi + f + 0.06, d0 - 0.03, d1 + 0.03, z1 + f, z1 + f + 0.12)
    coll = DETAIL_COLL % level.name
    doors.finish("%s_DoorFrames" % prefix, coll, "M_Wood_Dark" if level.key != "B1" else "M_Wood_Worn", {"hos_type": "frame", "hos_level": level.key})
    metal.finish("%s_DoorFrames_Metal" % prefix, coll, "M_Metal_Rust", {"hos_type": "frame", "hos_level": level.key})
    windows.finish("%s_WindowFrames" % prefix, "EXTERIOR/Windows", "M_Wood_Dark", {"hos_type": "frame", "hos_level": level.key})
    stone.finish("%s_WindowStone" % prefix, "EXTERIOR/Decorations", "M_Stone_Old", {"hos_type": "decor", "hos_level": level.key})


# =============================================================================
#  PLINTHES, CORNICHES, LAMBRIS
# =============================================================================

def build_trims(level, prefix):
    if level.key in ("B1", "TUN", "SHED"):
        return
    skirt, cornice, wains = MeshAcc(), MeshAcc(), MeshAcc()
    door_kinds = set(k for k in DOOR_KINDS)
    wainscot_rooms = WAINSCOT_ROOMS.get(level.key, set())
    for room in level.rooms:
        if not room.floor:
            continue
        ext_sides = exterior_sides(level, room)
        for side in ("S", "N", "W", "E"):
            axis, line, a, b = room.edge(side)
            sign = +1 if side in ("S", "W") else -1
            thk = level.ext_thk if side in ext_sides else level.int_thk
            face = sign * thk / 2
            # raccourcir aux angles de la piece (epaisseur des murs perpendiculaires)
            perp = {"S": ("W", "E"), "N": ("W", "E"), "W": ("S", "N"), "E": ("S", "N")}[side]
            ins0 = (level.ext_thk if perp[0] in ext_sides else level.int_thk) / 2
            ins1 = (level.ext_thk if perp[1] in ext_sides else level.int_thk) / 2
            for (ia, ib) in wall_intervals(level, room, side):
                u0 = ia + (ins0 if abs(ia - a) < 1e-6 else 0.0)
                u1 = ib - (ins1 if abs(ib - b) < 1e-6 else 0.0)
                if u1 - u0 < 0.05:
                    continue
                door_cuts = [(lo - 0.08, hi + 0.08) for lo, hi, op in openings_on(level, axis, line, u0, u1, door_kinds)]
                win_cuts = [(lo - 0.08, hi + 0.08) for lo, hi, op in openings_on(level, axis, line, u0, u1, {"window"})]

                def spans(cuts):
                    out, cur = [], u0
                    for lo, hi in sorted(cuts):
                        if lo > cur:
                            out.append((cur, min(lo, u1)))
                        cur = max(cur, hi)
                    if cur < u1:
                        out.append((cur, u1))
                    return [s for s in out if s[1] - s[0] > 0.02]

                for s0, s1 in spans(door_cuts):
                    along_box(skirt, axis, line, s0, s1, face, face + sign * 0.02, level.z, level.z + 0.14)
                if level.key in ("GF", "F1") and room.name not in NO_CORNICE:
                    along_box(cornice, axis, line, u0, u1, face, face + sign * 0.14, level.ceiling_z - 0.16, level.ceiling_z)
                    along_box(cornice, axis, line, u0, u1, face, face + sign * 0.06, level.ceiling_z - 0.30, level.ceiling_z - 0.16)
                if room.name in wainscot_rooms:
                    for s0, s1 in spans(door_cuts + win_cuts):
                        along_box(wains, axis, line, s0, s1, face, face + sign * 0.03, level.z + 0.14, level.z + 0.86)
                        along_box(wains, axis, line, s0, s1, face, face + sign * 0.05, level.z + 0.86, level.z + 0.92)
    coll = DETAIL_COLL % level.name
    mat_wood = "M_Wood_Worn" if level.key == "F2" else "M_Wood_Dark"
    skirt.finish("%s_Skirting" % prefix, coll, mat_wood, {"hos_type": "trim", "hos_level": level.key})
    cornice.finish("%s_Cornice" % prefix, coll, "M_Plaster_Old", {"hos_type": "trim", "hos_level": level.key})
    wains.finish("%s_Wainscot" % prefix, coll, "M_Wood_Dark", {"hos_type": "trim", "hos_level": level.key})


# =============================================================================
#  POUTRES
# =============================================================================

def build_beams(level, prefix):
    rooms = BEAM_ROOMS.get(level.key)
    if rooms is None and level.key not in BEAM_ROOMS:
        return
    acc = MeshAcc()
    for room in level.rooms:
        if not room.floor or (rooms is not None and room.name not in rooms):
            continue
        if room.name.startswith("Esc"):
            continue
        x0, x1, y0, y1 = room.rect
        w, d = x1 - x0, y1 - y0
        zc = level.ceiling_z
        along_x = d <= w   # les poutres franchissent la petite portee
        span_len = d if along_x else w
        count = max(1, int((w if along_x else d) / 1.5))
        for i in range(1, count):
            t = i / count
            if along_x:
                x = x0 + t * w
                acc.box(x - 0.1, x + 0.1, y0 + 0.1, y1 - 0.1, zc - 0.26, zc)
            else:
                y = y0 + t * d
                acc.box(x0 + 0.1, x1 - 0.1, y - 0.1, y + 0.1, zc - 0.26, zc)
    acc.finish("%s_Beams" % prefix, DETAIL_COLL % level.name, "M_Wood_Worn", {"hos_type": "beam", "hos_level": level.key})


# =============================================================================
#  CHEMINEES INTERIEURES
# =============================================================================

def build_fireplaces(levels):
    by_key = {lv.key: lv for lv in levels}
    n = 0
    for key, room_name, side, t in FIREPLACES:
        level = by_key[key]
        room = level.room(room_name)
        axis, line, a, b, sign, thk = side_frame(room, side, level)
        cx = a + t * (b - a)
        face = sign * thk / 2
        z0, zc = level.z, level.ceiling_z
        acc = MeshAcc()
        # massif : jambages, hotte, panneau de fond, atre, tablette
        along_box(acc, axis, line, cx - 0.95, cx - 0.55, face, face + sign * 0.5, z0, zc)
        along_box(acc, axis, line, cx + 0.55, cx + 0.95, face, face + sign * 0.5, z0, zc)
        along_box(acc, axis, line, cx - 0.55, cx + 0.55, face, face + sign * 0.5, z0 + 1.05, zc)
        along_box(acc, axis, line, cx - 0.55, cx + 0.55, face, face + sign * 0.12, z0, z0 + 1.05)
        along_box(acc, axis, line, cx - 1.15, cx + 1.15, face, face + sign * 0.8, z0, z0 + 0.04)
        along_box(acc, axis, line, cx - 1.05, cx + 1.05, face, face + sign * 0.6, z0 + 1.32, z0 + 1.40)
        along_box(acc, axis, line, cx - 1.0, cx + 1.0, face + sign * 0.5, face + sign * 0.56, z0 + 1.05, z0 + 1.32)
        acc.finish("%s_Fireplace_%s" % (key, room_name), DETAIL_COLL % level.name, "M_Stone_Old",
                   {"hos_type": "fireplace", "hos_room": room_name, "hos_level": key})
        n += 1
    return "Cheminees interieures : %d" % n


# =============================================================================
#  EXTERIEUR : LUCARNES, BANDEAUX, SOUBASSEMENT
# =============================================================================

def build_exterior_details(levels):
    B1, GF, F1, F2, TUN, SHED = levels
    z_eave = F2.ext_top
    slope = math.tan(math.radians(35))

    def dormer(name, cx, north):
        acc = MeshAcc()
        if north:
            y_front, y_back = 30.3, 27.4
            ya, yb = y_back, y_front
        else:
            y_front, y_back = -0.3, 2.6
            ya, yb = y_front, y_back
        acc.box(cx - 0.9, cx + 0.9, ya, yb, z_eave, z_eave + 2.1)
        # toit a deux pentes de la lucarne
        v = [(cx - 1.0, ya, z_eave + 2.1), (cx + 1.0, ya, z_eave + 2.1), (cx, ya, z_eave + 2.9),
             (cx - 1.0, yb, z_eave + 2.1), (cx + 1.0, yb, z_eave + 2.1), (cx, yb, z_eave + 2.9)]
        f = [(0, 1, 2), (5, 4, 3), (0, 2, 5, 3), (1, 4, 5, 2), (0, 3, 4, 1)]
        acc.poly(v, f)
        obj = acc.finish(name, "EXTERIOR/Roof", "M_Stone_Old", {"hos_type": "decor"})
        g = MeshAcc()
        yg0, yg1 = (y_front - 0.02, y_front + 0.02)
        g.box(cx - 0.45, cx + 0.45, yg0, yg1, z_eave + 0.6, z_eave + 1.8)
        g.finish(name + "_Glass", "EXTERIOR/Windows", "M_Glass_Dirty", {"hos_type": "window"})
        return obj

    for i, cx in enumerate(DORMERS_S):
        dormer("EXT_Dormer_S%d" % (i + 1), cx, False)
    for i, cx in enumerate(DORMERS_N):
        dormer("EXT_Dormer_N%d" % (i + 1), cx, True)

    # Bandeaux a chaque niveau et soubassement
    acc = MeshAcc()
    for z in (F1.z - 0.25, F2.z - 0.25):
        acc.box(-0.45, 52.45, -0.45, -0.3, z, z + 0.25)
        acc.box(-0.45, 52.45, 30.3, 30.45, z, z + 0.25)
        acc.box(-0.45, -0.3, -0.45, 30.45, z, z + 0.25)
        acc.box(52.3, 52.45, -0.45, 30.45, z, z + 0.25)
    acc.finish("EXT_StringCourses", "EXTERIOR/Decorations", "M_Stone_Old", {"hos_type": "decor"})
    acc = MeshAcc()
    acc.box(-0.5, 21.0, -0.5, -0.3, GROUND_Z, 0.3)      # sud, interrompu au porche
    acc.box(31.0, 52.5, -0.5, -0.3, GROUND_Z, 0.3)
    acc.box(-0.5, 47.3, 30.3, 30.5, GROUND_Z, 0.3)      # nord, interrompu aux marches de service
    acc.box(50.7, 52.5, 30.3, 30.5, GROUND_Z, 0.3)
    acc.box(-0.5, -0.3, -0.5, 30.5, GROUND_Z, 0.3)      # ouest
    acc.box(52.3, 52.5, 10.3, 30.5, GROUND_Z, 0.3)      # est, interrompu a la terrasse
    acc.finish("EXT_Plinth", "EXTERIOR/Decorations", "M_Stone_Old", {"hos_type": "decor"})

    # Corniche d'egout sous le toit
    acc = MeshAcc()
    acc.box(-0.6, 52.6, -0.6, -0.3, z_eave - 0.4, z_eave)
    acc.box(-0.6, 52.6, 30.3, 30.6, z_eave - 0.4, z_eave)
    acc.box(-0.6, -0.3, -0.6, 30.6, z_eave - 0.4, z_eave)
    acc.box(52.3, 52.6, -0.6, 30.6, z_eave - 0.4, z_eave)
    acc.finish("EXT_EaveCornice", "EXTERIOR/Decorations", "M_Stone_Old", {"hos_type": "decor"})
    return "Lucarnes : %d, bandeaux et soubassement" % (len(DORMERS_S) + len(DORMERS_N))


# =============================================================================
#  POINT D'ENTREE
# =============================================================================

def build_details(levels):
    reports = []
    for lv in levels:
        prefix = lv.key
        get_collection(DETAIL_COLL % lv.name)
        build_frames(lv, prefix)
        build_trims(lv, prefix)
        build_beams(lv, prefix)
    reports.append(build_railings(levels))
    reports.append(build_columns_and_vaults(levels))
    reports.append(build_fireplaces(levels))
    reports.append(build_exterior_details(levels))
    return reports
