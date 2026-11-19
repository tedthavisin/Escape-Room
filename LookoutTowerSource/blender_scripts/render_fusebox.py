"""Preview renders of the fuse box set inside the rebuilt cab.
usage: python render_fusebox.py <blend> <tex_dir> <out_prefix> <view>[,<view>...] [samples] [--lit]"""
import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
from mathutils import Vector
import bgeo
import build_tower as T
# material table + world/camera helpers, same as render_preview.py (which runs on import, so it is copied here)

MATS = {
    "M_Timber": ("Timber", 1.0), "M_PlanksWeathered": ("PlanksWeathered", 2.0),
    "M_PlanksInterior": ("PlanksInterior", 2.0), "M_Siding": ("Siding", 2.0), "M_TrimGreen": ("TrimGreen", 1.0),
    "M_TrimCream": ("TrimCream", 1.0), "M_Paneling": ("Paneling", 2.0), "M_Roof": ("RoofShakes", 2.0),
    "M_Iron": ("Iron", 1.0), "M_Concrete": ("Concrete", 1.0), "M_Glass": ("GlassGrime", 1.0),
    "M_Brass": ("Brass", 0.5), "M_Wool": ("Wool", 0.5), "M_Ticking": ("Ticking", 1.0), "M_Map": ("Map", 1.0),
    "M_PaintedMetal": ("PaintedMetal", 0.5), "M_WoodFurniture": ("WoodFurniture", 1.0), "M_Bark": ("Bark", 1.0),
    "M_Needles": ("Needles", 1.0), "M_Rock": ("Rock", 2.0), "M_ForestFloor": ("ForestFloor", 4.0),
}
PLAIN0 = {
    "M_PlasticBlack": dict(color=(0.03, 0.03, 0.03, 1), rough=0.45),
    "M_DialGlow": dict(color=(1.0, 0.6, 0.25, 1), emission=(1.0, 0.55, 0.2, 3.0)),
    "M_LanternGlass": dict(color=(1.0, 0.8, 0.5, 1), emission=(1.0, 0.65, 0.3, 6.0)),
    "M_GlassInsulator": dict(color=(0.35, 0.6, 0.45, 1), rough=0.1),
    "M_BookCover": dict(color=(0.35, 0.08, 0.06, 1), rough=0.7),
    "M_Paper": dict(color=(0.85, 0.82, 0.72, 1), rough=0.9),
    "M_RedPaint": dict(color=(0.45, 0.06, 0.04, 1), rough=0.5),
    "COL": dict(color=(1, 0, 1, 1)),
}


def setup_world(night=False):
    sc = bpy.context.scene
    w = bpy.data.worlds.new("W")
    sc.world = w
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.55, 0.65, 0.8, 1) if not night else (0.02, 0.03, 0.05, 1)
    bg.inputs[1].default_value = 0.9 if not night else 0.4
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 3.2 if not night else 0.25
    sun.color = (1.0, 0.95, 0.88) if not night else (0.6, 0.7, 1.0)
    sun.angle = math.radians(3)
    so = bpy.data.objects.new("Sun", sun)
    sc.collection.objects.link(so)
    so.rotation_euler = (math.radians(50), 0, math.radians(35))


def look(cam, target):
    d = Vector(target) - cam.location
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


F = T.FLOOR_Z
PLAIN = dict(PLAIN0)
PLAIN.update({
    "M_Porcelain": dict(color=(0.86, 0.84, 0.78, 1), rough=0.15),
    "M_FuseBand": dict(color=(0.1, 0.25, 0.75, 1), rough=0.4),
    "M_FuseWindow": dict(color=(0.62, 0.66, 0.68, 1), rough=0.05),
    "M_FuseLink": dict(color=(0.72, 0.72, 0.74, 1), rough=0.3),
    "M_Enamel": dict(color=(0.12, 0.2, 0.14, 1), rough=0.2),
    "M_EnamelWhite": dict(color=(0.85, 0.85, 0.8, 1), rough=0.25),
    "M_Bulb": dict(color=(0.95, 0.88, 0.7, 1), rough=0.2),
})

VIEWS = {
    "panel": dict(loc=(0.75, -2.75, F + 1.62), target=(0.55, -3.47, F + 1.38), lens=30),
    "panel_open": dict(loc=(0.42, -2.95, F + 1.52), target=(0.58, -3.46, F + 1.42), lens=32),
    "south": dict(loc=(0.9, -0.2, F + 1.65), target=(0.45, -3.5, F + 1.7), lens=17),
    "radio": dict(loc=(2.0, 1.35, F + 1.45), target=(3.35, 0.45, F + 0.85), lens=24),
    "tin": dict(loc=(2.45, -0.95, F + 1.5), target=(3.38, -0.28, F + 0.78), lens=30),
    "crate": dict(loc=(0.35, 1.85, F + 1.65), target=(-0.1, 3.3, F + 0.85), lens=26),
    "lamp_out": dict(loc=(1.3, -5.3, F + 1.55), target=(0.15, -3.7, F + 2.05), lens=24),
    "pendant": dict(loc=(1.7, -1.9, F + 1.45), target=(0.05, 0.0, F + 2.2), lens=22),
    "fuse": dict(loc=(0.055, -0.07, 0.07), target=(0.0, 0.0, 0.016), lens=60),
}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    blend, tex_dir, prefix, views = argv[0], argv[1], argv[2], argv[3].split(",")
    samples = int(argv[4]) if len(argv) > 4 and argv[4].isdigit() else 32
    lit = "--lit" in argv
    bpy.ops.wm.open_mainfile(filepath=blend)
    for name, (tex, tile) in MATS.items():
        if os.path.exists(f"{tex_dir}/T_{tex}_Albedo.png"):
            bgeo.register_material(name, tex, tile, alpha=(name == "M_Glass"))
        else:
            bgeo.register_material(name, None, 1.0, color=(0.3, 0.32, 0.25, 1))
    for name, kw in PLAIN.items():
        em = kw.get("emission")
        if lit and name == "M_Bulb":
            em = (1.0, 0.75, 0.45, 25.0)
        bgeo.register_material(name, None, 1.0, color=kw.get("color", (0.8, 0.8, 0.8, 1)), emission=em,
                               rough=kw.get("rough", 0.6))
    bgeo.setup_preview_materials(tex_dir)
    for o in bpy.data.objects:
        if o.name.startswith("COL"):
            o.hide_render = True
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.cycles.max_bounces = 5
    sc.render.resolution_x, sc.render.resolution_y = 800, 540
    sc.view_settings.view_transform = "AgX"
    setup_world(night=lit)
    if not lit:
        ad = bpy.data.lights.new("Fill", "AREA")
        ad.energy = 260
        ad.size = 3.0
        ad.color = (1.0, 0.97, 0.92)
        ao = bpy.data.objects.new("Fill", ad)
        ao.location = (0.05, 0.0, F + 2.6)
        sc.collection.objects.link(ao)
        out = bpy.data.lights.new("OutFill", "AREA")
        out.energy = 200
        out.size = 4.0
        oo = bpy.data.objects.new("OutFill", out)
        oo.location = (0.5, -6.5, F + 2.5)
        oo.rotation_euler = (math.radians(70), 0, 0)
        sc.collection.objects.link(oo)
    if lit:
        for name, loc, energy in (("Lc", (0.05, 0.0, F + 2.2), 60), ("Lo", (0.0, -3.82, F + 2.2), 25)):
            ld = bpy.data.lights.new(name, "POINT")
            ld.energy = energy
            ld.color = (1.0, 0.78, 0.5)
            ld.shadow_soft_size = 0.03
            lo = bpy.data.objects.new(name, ld)
            lo.location = loc
            sc.collection.objects.link(lo)
    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    # drop a few fuse instances where Unity puts them, for the preview
    fz = bpy.data.objects.get("Fuse")
    if fz is not None:
        for mk, rot in (("MK_Socket_1", (-90, 0, 0)), ("MK_Socket_2", (-90, 0, 30)), ("MK_TinSpot_1", (0, 0, 0)),
                        ("MK_CrateSpot_1", (0, 90, 20)), ("MK_CrateSpot_2", (0, 90, -40))):
            e = bpy.data.objects.get(mk)
            if e is None:
                continue
            d = fz.copy()
            d.location = e.location + (Vector((0, 0, 0.016)) if mk.startswith("MK_Crate") else Vector())
            d.rotation_euler = tuple(math.radians(a) for a in rot)
            sc.collection.objects.link(d)
        fz.location = (0.0, 0.0, 0.0)
    door = bpy.data.objects.get("FuseBox_Door")
    for v in views:
        p = VIEWS[v]
        if door is not None:
            door.rotation_euler = (0, 0, math.radians(-105) if v == "panel_open" else 0)
        cam.location = Vector(p["loc"])
        look(cam, p["target"])
        cam_data.lens = p["lens"]
        cam_data.clip_start = 0.01
        sc.render.filepath = f"{prefix}_{v}.png"
        bpy.ops.render.render(write_still=True)
        print("rendered", v, flush=True)


main()
