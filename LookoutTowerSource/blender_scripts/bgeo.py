"""Small procedural-geometry kit on top of bpy.
Coordinates are Blender world space (Z up, metres). UVs are in metres unless a mode says otherwise.
Every face gets its own vertices for hard edges (flat), except where `smooth` sharing is requested."""
import math
import bpy
import numpy as np
from mathutils import Vector, Matrix

V = Vector


def vec(*a):
    if len(a) == 1:
        a = a[0]
    return Vector((float(a[0]), float(a[1]), float(a[2])))


class MB:
    """Mesh builder: collects polygons with UVs and material names."""

    def __init__(self, name, origin=(0, 0, 0)):
        self.name = name
        self.origin = vec(origin)
        self.verts = []
        self.faces = []
        self.uvs = []
        self.mats = []
        self.smooth = []
        self._rng = np.random.default_rng(abs(hash(name)) % (2 ** 32))

    # ------------------------------------------------------------------ primitives
    def poly(self, pts, uvs, mat, smooth=False):
        base = len(self.verts)
        self.verts.extend([vec(p) for p in pts])
        self.faces.append(list(range(base, base + len(pts))))
        self.uvs.append([tuple(u) for u in uvs])
        self.mats.append(mat)
        self.smooth.append(smooth)

    def rand_off(self, scale=4.0):
        return self._rng.uniform(0, scale), self._rng.uniform(0, scale)

    def obox(self, o, ex, ey, ez, sx, sy, sz, mat, uv="len", uvoff=None, faces="all", unit_axes=None):
        """Oriented box. o = min corner; ex,ey,ez right-handed unit axes; sizes along each.
        uv='len': U along ex on the four long faces (grain follows ex), V across.
        uv='unit': each face mapped 0..1. faces: 'all' or a set of '+x','-x','+y','-y','+z','-z'."""
        o, ex, ey, ez = vec(o), vec(ex), vec(ey), vec(ez)
        if uvoff is None:
            uvoff = self.rand_off()
        uo, vo = uvoff

        def P(x, y, z):
            return o + ex * x + ey * y + ez * z

        defs = {
            "+x": [(sx, 0, 0), (sx, sy, 0), (sx, sy, sz), (sx, 0, sz)],
            "-x": [(0, 0, 0), (0, 0, sz), (0, sy, sz), (0, sy, 0)],
            "+y": [(0, sy, 0), (0, sy, sz), (sx, sy, sz), (sx, sy, 0)],
            "-y": [(0, 0, 0), (sx, 0, 0), (sx, 0, sz), (0, 0, sz)],
            "+z": [(0, 0, sz), (sx, 0, sz), (sx, sy, sz), (0, sy, sz)],
            "-z": [(0, 0, 0), (0, sy, 0), (sx, sy, 0), (sx, 0, 0)],
        }
        for k, cs in defs.items():
            if faces != "all" and k not in faces:
                continue
            pts = [P(*c) for c in cs]
            if uv == "unit":
                ax = {"+x": (1, 2), "-x": (1, 2), "+y": (0, 2), "-y": (0, 2), "+z": (0, 1), "-z": (0, 1)}[k]
                if unit_axes and k in unit_axes:
                    ax = unit_axes[k]
                size = (sx, sy, sz)
                uvs = [(c[ax[0]] / size[ax[0]], c[ax[1]] / size[ax[1]]) for c in cs]
            else:
                if k in ("+y", "-y"):
                    uvs = [(c[0] + uo, c[2] + vo) for c in cs]
                elif k in ("+z", "-z"):
                    uvs = [(c[0] + uo, c[1] + vo) for c in cs]
                else:
                    uvs = [(c[1] + uo, c[2] + vo) for c in cs]
            self.poly(pts, uvs, mat)

    def box(self, mn, mx, mat, uv="len", grain="x", uvoff=None, faces="all"):
        """Axis-aligned box from min to max corner. grain: world axis that U follows."""
        a, b = vec(mn), vec(mx)
        mn = vec(min(a.x, b.x), min(a.y, b.y), min(a.z, b.z))
        mx = vec(max(a.x, b.x), max(a.y, b.y), max(a.z, b.z))
        s = mx - mn
        if grain == "x":
            self.obox(mn, (1, 0, 0), (0, 1, 0), (0, 0, 1), s.x, s.y, s.z, mat, uv, uvoff, faces)
        elif grain == "y":
            # ex=+Y, ey=-X, ez=+Z  (right-handed), min corner shifts to (max.x, min.y, min.z)
            f = None if faces == "all" else {_map_face_y(k) for k in faces}
            self.obox((mx.x, mn.y, mn.z), (0, 1, 0), (-1, 0, 0), (0, 0, 1), s.y, s.x, s.z, mat, uv, uvoff,
                      "all" if f is None else f)
        elif grain == "z":
            # ex=+Z, ey=+X, ez=+Y
            f = None if faces == "all" else {_map_face_z(k) for k in faces}
            self.obox(mn, (0, 0, 1), (1, 0, 0), (0, 1, 0), s.z, s.x, s.y, mat, uv, uvoff,
                      "all" if f is None else f)

    def beam(self, p0, p1, w, d, mat, up=(0, 0, 1), uvoff=None, extend=0.0, faces="all"):
        """Rectangular member from p0 to p1 (centre line). w across (ey), d along ~up (ez)."""
        p0, p1 = vec(p0), vec(p1)
        ax = p1 - p0
        L = ax.length
        ex = ax / L
        u = vec(up)
        if abs(ex.dot(u.normalized())) > 0.97:
            u = vec(1, 0, 0) if abs(ex.x) < 0.9 else vec(0, 1, 0)
        ey = u.cross(ex).normalized()
        ez = ex.cross(ey)
        o = p0 - ex * extend - ey * (w / 2) - ez * (d / 2)
        self.obox(o, ex, ey, ez, L + 2 * extend, w, d, mat, "len", uvoff, faces)

    def cyl(self, p0, p1, r0, r1=None, segs=12, mat="M_Iron", caps=(True, True), smooth=True, uvoff=None,
            ang0=0.0, cap_mat=None, uv_scale=1.0):
        p0, p1 = vec(p0), vec(p1)
        r1 = r0 if r1 is None else r1
        ax = p1 - p0
        L = ax.length
        ez = ax / L
        t = vec(1, 0, 0) if abs(ez.x) < 0.9 else vec(0, 1, 0)
        ex = t.cross(ez).normalized()
        ey = ez.cross(ex)
        if uvoff is None:
            uvoff = self.rand_off()
        uo, vo = uvoff
        circ = 2 * math.pi * max(r0, r1)
        ring0, ring1 = [], []
        for i in range(segs + 1):
            a = ang0 + 2 * math.pi * i / segs
            d = ex * math.cos(a) + ey * math.sin(a)
            ring0.append(p0 + d * r0)
            ring1.append(p1 + d * r1)
        base = len(self.verts)
        # shared ring vertices for smooth sides (seam duplicated at i=segs)
        self.verts.extend(ring0 + ring1)
        n = segs + 1
        for i in range(segs):
            a, b = base + i, base + i + 1
            c, d_ = base + n + i + 1, base + n + i
            self.faces.append([a, b, c, d_])
            u0 = uvoff[0] + circ * i / segs * uv_scale
            u1 = uvoff[0] + circ * (i + 1) / segs * uv_scale
            self.uvs.append([(u0, vo), (u1, vo), (u1, vo + L * uv_scale), (u0, vo + L * uv_scale)])
            self.mats.append(mat)
            self.smooth.append(smooth)
        cm = cap_mat or mat
        if caps[0]:
            pts = list(reversed(ring0[:-1]))
            self.poly(pts, [((p - p0).dot(ex) + uo, (p - p0).dot(ey) + vo) for p in pts], cm)
        if caps[1]:
            pts = ring1[:-1]
            self.poly(pts, [((p - p1).dot(ex) + uo, (p - p1).dot(ey) + vo) for p in pts], cm)

    def tube_path(self, pts, r, segs=8, mat="M_Iron", caps=True):
        for a, b in zip(pts[:-1], pts[1:]):
            self.cyl(a, b, r, segs=segs, mat=mat, caps=(caps, caps))

    def quad(self, a, b, c, d, mat, uvmode="planar", uvoff=(0, 0), ux=None, uy=None):
        pts = [vec(a), vec(b), vec(c), vec(d)]
        if uvmode == "unit":
            uvs = [(0, 0), (1, 0), (1, 1), (0, 1)]
        else:
            ux = vec(ux) if ux is not None else (pts[1] - pts[0]).normalized()
            n = (pts[1] - pts[0]).cross(pts[3] - pts[0]).normalized()
            uy = vec(uy) if uy is not None else n.cross(ux).normalized()
            uvs = [((p - pts[0]).dot(ux) + uvoff[0], (p - pts[0]).dot(uy) + uvoff[1]) for p in pts]
        self.poly(pts, uvs, mat)

    def tri(self, a, b, c, mat, ux=None, uy=None, uvoff=(0, 0), origin=None):
        pts = [vec(a), vec(b), vec(c)]
        n = (pts[1] - pts[0]).cross(pts[2] - pts[0]).normalized()
        ux = vec(ux) if ux is not None else (pts[1] - pts[0]).normalized()
        uy = vec(uy) if uy is not None else n.cross(ux).normalized()
        o = vec(origin) if origin is not None else pts[0]
        self.poly(pts, [((p - o).dot(ux) + uvoff[0], (p - o).dot(uy) + uvoff[1]) for p in pts], mat)

    def disc(self, c, r, segs, mat, normal=(0, 0, 1), uvmode="unit", ang0=0.0):
        c = vec(c)
        nz = vec(normal).normalized()
        t = vec(1, 0, 0) if abs(nz.x) < 0.9 else vec(0, 1, 0)
        ex = t.cross(nz).normalized()
        ey = nz.cross(ex)
        pts = []
        uvs = []
        for i in range(segs):
            a = ang0 + 2 * math.pi * i / segs
            d = ex * math.cos(a) + ey * math.sin(a)
            pts.append(c + d * r)
            if uvmode == "unit":
                uvs.append((0.5 + 0.5 * math.cos(a), 0.5 + 0.5 * math.sin(a)))
            else:
                uvs.append(((d * r).dot(ex), (d * r).dot(ey)))
        self.poly(pts, uvs, mat)

    def ring(self, c, r_in, r_out, z0, z1, segs, mat, uvoff=None):
        """Flat annulus solid (washer) around Z axis at centre c."""
        c = vec(c)
        for i in range(segs):
            a0 = 2 * math.pi * i / segs
            a1 = 2 * math.pi * (i + 1) / segs
            d0, d1 = vec(math.cos(a0), math.sin(a0), 0), vec(math.cos(a1), math.sin(a1), 0)
            ti, to = c + d0 * r_in, c + d0 * r_out
            ti1, to1 = c + d1 * r_in, c + d1 * r_out
            Z0, Z1 = vec(0, 0, z0), vec(0, 0, z1)
            self.quad(ti + Z1, to + Z1, to1 + Z1, ti1 + Z1, mat, ux=(1, 0, 0), uy=(0, 1, 0))
            self.quad(ti + Z0, ti1 + Z0, to1 + Z0, to + Z0, mat, ux=(1, 0, 0), uy=(0, 1, 0))
            self.quad(to + Z0, to1 + Z0, to1 + Z1, to + Z1, mat)
            self.quad(ti1 + Z0, ti + Z0, ti + Z1, ti1 + Z1, mat)

    def extrude_poly(self, pts2d, z0, z1, mat, side_mat=None, frame=None, uvoff=(0, 0)):
        """Extrude a CCW 2-D polygon (x,y) between z0 and z1 (in frame: origin, ex, ey, ez)."""
        if frame is None:
            frame = (vec(0, 0, 0), vec(1, 0, 0), vec(0, 1, 0), vec(0, 0, 1))
        o, ex, ey, ez = [vec(f) for f in frame]
        P = lambda x, y, z: o + ex * x + ey * y + ez * z
        top = [P(x, y, z1) for x, y in pts2d]
        bot = [P(x, y, z0) for x, y in reversed(pts2d)]
        self.poly(top, [(x + uvoff[0], y + uvoff[1]) for x, y in pts2d], mat)
        self.poly(bot, [(x + uvoff[0], y + uvoff[1]) for x, y in reversed(pts2d)], mat)
        sm = side_mat or mat
        n = len(pts2d)
        acc = 0.0
        for i in range(n):
            (x0, y0), (x1, y1) = pts2d[i], pts2d[(i + 1) % n]
            L = math.hypot(x1 - x0, y1 - y0)
            self.poly([P(x0, y0, z0), P(x1, y1, z0), P(x1, y1, z1), P(x0, y0, z1)],
                      [(acc, z0), (acc + L, z0), (acc + L, z1), (acc, z1)], sm)
            acc += L

    def merge(self, other):
        base = len(self.verts)
        self.verts.extend(other.verts)
        for f in other.faces:
            self.faces.append([i + base for i in f])
        self.uvs.extend(other.uvs)
        self.mats.extend(other.mats)
        self.smooth.extend(other.smooth)

    def transform(self, M):
        M = Matrix(M)
        self.verts = [M @ v for v in self.verts]

    # ------------------------------------------------------------------ to Blender
    def build(self, collection=None, merge_dist=0.0):
        mats = []
        for m in self.mats:
            if m not in mats:
                mats.append(m)
        me = bpy.data.meshes.new(self.name)
        verts = [tuple(v - self.origin) for v in self.verts]
        me.from_pydata(verts, [], self.faces)
        me.update()
        uvl = me.uv_layers.new(name="UVMap")
        li = 0
        for poly, uvs in zip(me.polygons, self.uvs):
            for k, loop_index in enumerate(poly.loop_indices):
                uvl.data[loop_index].uv = uvs[k]
        for m in mats:
            me.materials.append(get_mat(m))
        for poly, m, s in zip(me.polygons, self.mats, self.smooth):
            poly.material_index = mats.index(m)
        me.shade_smooth()
        me.validate(clean_customdata=False)
        ob = bpy.data.objects.new(self.name, me)
        ob.location = self.origin
        (collection or bpy.context.scene.collection).objects.link(ob)
        if merge_dist > 0:
            import bmesh
            bm = bmesh.new()
            bm.from_mesh(me)
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=merge_dist)
            bm.to_mesh(me)
            bm.free()
        return ob

    def tri_count(self):
        return sum(len(f) - 2 for f in self.faces)


def _map_face_y(k):
    # grain=y box: local x=+Y, local y=-X, local z=+Z
    return {"+y": "+x", "-y": "-x", "-x": "+y", "+x": "-y", "+z": "+z", "-z": "-z"}[k]


def _map_face_z(k):
    # grain=z: local x=+Z, local y=+X, local z=+Y
    return {"+z": "+x", "-z": "-x", "+x": "+y", "-x": "-y", "+y": "+z", "-y": "-z"}[k]


# ---------------------------------------------------------------------- materials
MAT_TEX = {}  # material name -> (texture base name, tile metres, extra dict)


def register_material(name, tex=None, tile=1.0, color=(0.8, 0.8, 0.8, 1), alpha=False, emission=None,
                      metallic=0.0, rough=0.6):
    MAT_TEX[name] = dict(tex=tex, tile=tile, color=color, alpha=alpha, emission=emission, metallic=metallic,
                         rough=rough)


def get_mat(name):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    info = MAT_TEX.get(name, {})
    m.diffuse_color = info.get("color", (0.8, 0.8, 0.8, 1))
    return m


def setup_preview_materials(tex_dir):
    """Give each material a Principled node tree with the generated textures (for preview renders)."""
    import os
    for m in bpy.data.materials:
        info = MAT_TEX.get(m.name)
        if not info:
            continue
        m.use_nodes = True
        nt = m.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        bs = nt.nodes.new("ShaderNodeBsdfPrincipled")
        nt.links.new(bs.outputs[0], out.inputs[0])
        bs.inputs["Base Color"].default_value = info["color"]
        bs.inputs["Metallic"].default_value = info["metallic"]
        bs.inputs["Roughness"].default_value = info["rough"]
        tex = info["tex"]
        if tex:
            tc = nt.nodes.new("ShaderNodeTexCoord")
            mp = nt.nodes.new("ShaderNodeMapping")
            s = 1.0 / info["tile"]
            mp.inputs["Scale"].default_value = (s, s, 1)
            nt.links.new(tc.outputs["UV"], mp.inputs["Vector"])
            def img(path, noncolor=False):
                n = nt.nodes.new("ShaderNodeTexImage")
                n.image = bpy.data.images.load(path, check_existing=True)
                if noncolor:
                    n.image.colorspace_settings.name = "Non-Color"
                nt.links.new(mp.outputs[0], n.inputs[0])
                return n
            a = img(f"{tex_dir}/T_{tex}_Albedo.png")
            mix = nt.nodes.new("ShaderNodeMixRGB")
            mix.blend_type = "MULTIPLY"
            mix.inputs[0].default_value = 1.0
            mix.inputs[2].default_value = info["color"]
            nt.links.new(a.outputs["Color"], mix.inputs[1])
            nt.links.new(mix.outputs[0], bs.inputs["Base Color"])
            if info["alpha"]:
                nt.links.new(a.outputs["Alpha"], bs.inputs["Alpha"])
            p = f"{tex_dir}/T_{tex}_Normal.png"
            if os.path.exists(p):
                n = img(p, True)
                nm = nt.nodes.new("ShaderNodeNormalMap")
                nt.links.new(n.outputs["Color"], nm.inputs["Color"])
                nt.links.new(nm.outputs["Normal"], bs.inputs["Normal"])
            p = f"{tex_dir}/T_{tex}_Mask.png"
            if os.path.exists(p):
                k = img(p, True)
                sep = nt.nodes.new("ShaderNodeSeparateColor")
                nt.links.new(k.outputs["Color"], sep.inputs[0])
                inv = nt.nodes.new("ShaderNodeMath")
                inv.operation = "SUBTRACT"
                inv.inputs[0].default_value = 1.0
                nt.links.new(k.outputs["Alpha"], inv.inputs[1])
                nt.links.new(inv.outputs[0], bs.inputs["Roughness"])
                nt.links.new(sep.outputs[0], bs.inputs["Metallic"])
        if info["emission"]:
            bs.inputs["Emission Color"].default_value = info["emission"][:3] + (1,)
            bs.inputs["Emission Strength"].default_value = info["emission"][3]
        if info["alpha"] and not tex:
            bs.inputs["Alpha"].default_value = info["color"][3]


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    s = bpy.context.scene
    s.unit_settings.system = "METRIC"
    s.unit_settings.scale_length = 1.0


def export_fbx(path, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="Z", axis_up="Y",
        bake_space_transform=True, object_types={"MESH", "EMPTY"}, use_mesh_modifiers=True,
        mesh_smooth_type="OFF", use_tspace=False, path_mode="STRIP", embed_textures=False,
        add_leaf_bones=False, bake_anim=False, use_custom_props=False)
