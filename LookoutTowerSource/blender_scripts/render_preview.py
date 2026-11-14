"""Render preview images of a built .blend with the generated textures.
usage: python render_preview.py <blend> <tex_dir> <out_prefix> <view>[,<view>...] [samples]"""
import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
from mathutils import Vector
import bgeo

MATS = {
    "M_Timber": ("Timber", 1.0), "M_PlanksWeathered": ("PlanksWeathered", 2.0),
    "M_PlanksInterior": ("PlanksInterior", 2.0), "M_Siding": ("Siding", 2.0), "M_TrimGreen": ("TrimGreen", 1.0),
    "M_TrimCream": ("TrimCream", 1.0), "M_Paneling": ("Paneling", 2.0), "M_Roof": ("RoofShakes", 2.0),
    "M_Iron": ("Iron", 1.0), "M_Concrete": ("Concrete", 1.0), "M_Glass": ("GlassGrime", 1.0),
    "M_Brass": ("Brass", 0.5), "M_Wool": ("Wool", 0.5), "M_Ticking": ("Ticking", 1.0), "M_Map": ("Map", 1.0),
    "M_PaintedMetal": ("PaintedMetal", 0.5), "M_WoodFurniture": ("WoodFurniture", 1.0), "M_Bark": ("Bark", 1.0),
    "M_Needles": ("Needles", 1.0), "M_Rock": ("Rock", 2.0), "M_ForestFloor": ("ForestFloor", 4.0),
}
PLAIN = {
    "M_PlasticBlack": dict(color=(0.03, 0.03, 0.03, 1), rough=0.45),
    "M_DialGlow": dict(color=(1.0, 0.6, 0.25, 1), emission=(1.0, 0.55, 0.2, 3.0)),
    "M_LanternGlass": dict(color=(1.0, 0.8, 0.5, 1), emission=(1.0, 0.65, 0.3, 6.0)),
    "M_GlassInsulator": dict(color=(0.35, 0.6, 0.45, 1), rough=0.1),
    "M_BookCover": dict(color=(0.35, 0.08, 0.06, 1), rough=0.7),
    "M_Paper": dict(color=(0.85, 0.82, 0.72, 1), rough=0.9),
    "M_RedPaint": dict(color=(0.45, 0.06, 0.04, 1), rough=0.5),
    "COL": dict(color=(1, 0, 1, 1)),
}

VIEWS = {
    "tower": dict(loc=(14.0, -19.0, 4.0), target=(0, 0, 7.2), lens=28),
    "tower_low": dict(loc=(7.5, -9.0, 1.6), target=(0, 0, 5.5), lens=18),
    "cab": dict(loc=(6.5, -7.5, 12.6), target=(0, 0, 11.6), lens=30),
    "cab_ne": dict(loc=(-5.5, 7.0, 12.0), target=(0, 0, 11.8), lens=28),
    "interior": dict(loc=(1.6, -1.7, 12.15), target=(-1.2, 0.6, 10.9), lens=16),
    "interior2": dict(loc=(-1.7, 1.6, 12.1), target=(1.2, -1.0, 11.0), lens=16),
    "stairwell": dict(loc=(-1.25, -1.45, 1.6), target=(-0.6, 0.2, 7.0), lens=14),
    "underside": dict(loc=(3.4, -3.6, 7.6), target=(0, 0, 10.0), lens=20),
    "stairs_side": dict(loc=(7.5, -1.0, 5.2), target=(-0.8, 0.0, 5.0), lens=22),
    "hatch": dict(loc=(-0.2, 1.3, 12.2), target=(-1.2, -0.4, 9.0), lens=18),
    "stairs_top": dict(loc=(0.9, -1.4, 8.9), target=(-1.2, 0.2, 10.0), lens=14),
    "int_desk": dict(loc=(-0.9, -1.2, 12.05), target=(1.6, 0.3, 11.0), lens=17),
    "int_cot": dict(loc=(1.2, -1.5, 12.0), target=(-0.8, 1.3, 10.9), lens=17),
    "int_finder": dict(loc=(0.75, -0.55, 11.85), target=(0.15, 0.25, 11.45), lens=24),
    "int_south": dict(loc=(0.2, 1.4, 12.0), target=(-0.2, -2.0, 11.0), lens=17),
    "props": dict(loc=(3.0, -3.0, 2.2), target=(0, 0, 0.6), lens=30),
    "forest": dict(loc=(0, -52, 9), target=(0, 0, 8), lens=26),
    "forest_close": dict(loc=(-14, -16, 2.2), target=(-17, 0, 7), lens=20),
    "rocks": dict(loc=(-2, -24, 2.5), target=(-2, -14, 0.3), lens=30),
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


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    blend, tex_dir, prefix, views = argv[0], argv[1], argv[2], argv[3].split(",")
    samples = int(argv[4]) if len(argv) > 4 else 24
    bpy.ops.wm.open_mainfile(filepath=blend)
    for name, (tex, tile) in MATS.items():
        bgeo.register_material(name, tex, tile, alpha=(name == "M_Glass"))
    for name, kw in PLAIN.items():
        bgeo.register_material(name, None, 1.0, color=kw.get("color", (0.8, 0.8, 0.8, 1)), emission=kw.get("emission"),
                               rough=kw.get("rough", 0.6))
    bgeo.setup_preview_materials(tex_dir)
    hide = os.environ.get("HIDE", "").split(",")
    for o in bpy.data.objects:
        if o.name.startswith("COL") or o.name in hide:
            o.hide_render = True
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.cycles.max_bounces = 4
    sc.render.resolution_x, sc.render.resolution_y = 900, 600
    sc.view_settings.view_transform = "AgX"
    setup_world()
    # ground plane for context
    if "Ground" not in bpy.data.objects:
        bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0))
        g = bpy.context.active_object
        g.name = "Ground"
        mat = bpy.data.materials.get("M_ForestFloor") or bgeo.get_mat("M_ForestFloor")
        bgeo.register_material("M_ForestFloor", "ForestFloor", 4.0)
        g.data.materials.append(mat)
        bgeo.setup_preview_materials(tex_dir)
        # planar UVs in metres
        uv = g.data.uv_layers.new()
        for li, l in enumerate(g.data.loops):
            v = g.data.vertices[l.vertex_index].co
            uv.data[li].uv = (v.x, v.y)
    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    for v in views:
        p = VIEWS[v]
        cam.location = Vector(p["loc"])
        look(cam, p["target"])
        cam_data.lens = p["lens"]
        cam_data.clip_start = 0.05
        sc.render.filepath = f"{prefix}_{v}.png"
        bpy.ops.render.render(write_still=True)
        print("rendered", v, flush=True)


main()
