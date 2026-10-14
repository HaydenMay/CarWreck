"""Fixed-size mounting experiments, not a runtime customization framework."""
import hashlib
import math
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def mesh_revision(vertices, triangles):
    h = hashlib.sha256()
    h.update(np.asarray(vertices, dtype="<f8").tobytes())
    h.update(np.asarray(triangles, dtype="<i8").tobytes())
    return h.hexdigest()


def bind_anchors(vertices, triangles, seeds):
    """One rest-mesh BVH query per named seed; never query again after damage."""
    v = np.asarray(vertices, dtype=float)
    t = np.asarray(triangles, dtype=int)
    if not np.isfinite(v).all():
        raise ValueError("nonfinite surface")
    revision = mesh_revision(v,t)
    tree = BVHTree.FromPolygons([Vector(p) for p in v], t.tolist(), all_triangles=True)
    bindings = []
    for seed in seeds:
        direction = Vector(seed["direction"])
        if direction.length < 1e-8:
            raise ValueError("surface ray has no direction")
        hit, normal, index, distance = tree.ray_cast(
            Vector(seed["origin"]), direction.normalized(), seed["max_distance"])
        if hit is None:
            raise ValueError("missing surface for " + seed["id"])
        p = np.array(hit)
        low, high = np.asarray(seed["bounds"])
        expected = np.asarray(seed.get("expected_normal", [0,0,1]), dtype=float)
        expected /= np.linalg.norm(expected)
        if np.any(p < low) or np.any(p > high) or np.dot(normal,expected) < math.cos(math.radians(45)):
            raise ValueError("surface outside permitted patch/normal")
        indices = t[index]
        a,b,c = v[indices]
        ab,ac,ap = b-a,c-a,p-a
        gram = np.array([[ab@ab,ab@ac],[ab@ac,ac@ac]])
        if np.linalg.det(gram) < 1e-20:
            raise ValueError("collapsed surface triangle")
        w1,w2 = np.linalg.solve(gram,[ap@ab,ap@ac])
        bindings.append({"id":seed["id"],"revision":revision,
            "triangle_index":int(index),"vertex_indices":indices.tolist(),
            "weights":[float(1-w1-w2),float(w1),float(w2)],
            "rest_point":p.tolist(),"rest_normal":list(normal),
            "rest_double_area":float(np.linalg.norm(np.cross(ab,ac)))})
    return bindings


def sample_anchor(vertices, binding, revision):
    """Read only this triangle's three vertices from the current mesh."""
    if binding["revision"] != revision:
        raise ValueError("stale mesh revision")
    indices = binding["vertex_indices"]
    if len(indices) != 3 or min(indices) < 0 or max(indices) >= len(vertices):
        raise ValueError("anchor index outside mesh")
    tri = np.asarray([vertices[i] for i in indices], dtype=float)
    w = np.asarray(binding["weights"], dtype=float)
    if not np.isfinite(tri).all() or not np.isfinite(w).all():
        raise ValueError("nonfinite anchor")
    if abs(w.sum()-1) > 1e-6 or w.min() < -1e-5:
        raise ValueError("invalid barycentric weights")
    cross = np.cross(tri[1]-tri[0],tri[2]-tri[0])
    area = np.linalg.norm(cross)
    if area < max(1e-12,binding["rest_double_area"]*.01):
        raise ValueError("collapsed support triangle")
    return w @ tri, cross/area


def fit_rigid(local, targets):
    p,q = np.asarray(local,dtype=float),np.asarray(targets,dtype=float)
    if p.shape != q.shape or p.ndim != 2 or p.shape[1] != 3 or len(p)<3:
        raise ValueError("need matching support sets with at least three points")
    if not np.isfinite(p).all() or not np.isfinite(q).all():
        raise ValueError("nonfinite support")
    pc,qc = p.mean(axis=0),q.mean(axis=0)
    u,s,vt = np.linalg.svd((p-pc).T @ (q-qc))
    if s[0] < 1e-14 or s[1] < s[0]*1e-6:
        raise ValueError("collinear supports")
    r = vt.T @ u.T
    if np.linalg.det(r)<0:
        vt[-1] *= -1
        r = vt.T @ u.T
    pose = np.eye(4)
    pose[:3,:3],pose[:3,3] = r,qc-r@pc
    return pose


def transform(pose, points):
    return np.asarray(points) @ pose[:3,:3].T + pose[:3,3]


def assess(local, targets, pose, rest_targets, limits, *, rest_normals=None, normals=None, rest_pose=None):
    targets,rest = np.asarray(targets),np.asarray(rest_targets)
    residuals = np.linalg.norm(transform(pose,local)-targets,axis=1)
    ratios = []
    for i in range(len(rest)):
        for j in range(i):
            baseline = np.linalg.norm(rest[i]-rest[j])
            if baseline < 1e-8:
                raise ValueError("coincident rest supports")
            ratios.append(abs(np.linalg.norm(targets[i]-targets[j])/baseline-1))
    maximum = float(residuals.max())
    spacing = float(max(ratios))
    normal_error=0.
    if rest_normals is not None:
        if normals is None or rest_pose is None:
            raise ValueError("normal validation requires both frames and normal sets")
        rest_n,current_n=np.asarray(rest_normals),np.asarray(normals)
        if rest_n.shape!=targets.shape or current_n.shape!=targets.shape or not np.isfinite(rest_n).all() or not np.isfinite(current_n).all():
            raise ValueError("nonfinite or mismatched surface normals")
        transport=pose[:3,:3]@np.asarray(rest_pose)[:3,:3].T
        expected=rest_n@transport.T
        lengths=np.linalg.norm(expected,axis=1)*np.linalg.norm(current_n,axis=1)
        if np.any(lengths<1e-12):
            raise ValueError("collapsed surface normal")
        dots=np.sum(expected*current_n,axis=1)/lengths
        normal_error=float(np.degrees(np.arccos(np.clip(dots,-1,1))).max())
    status = ("invalid_support" if maximum>limits["invalid_m"] or spacing>limits["spacing_fraction"] or normal_error>limits.get("normal_deg",55)
              else "strained" if maximum>limits["strained_m"] or spacing>.1 else "valid")
    return {"status":status,"residuals_m":residuals.tolist(),
            "normal_error_deg":normal_error,
            "max_residual_m":maximum,"rms_residual_m":float(np.sqrt(np.mean(residuals**2))),
            "max_pairwise_spacing_change_fraction":spacing}


def select_adapter(adapters, vehicle, attachment, required):
    matches = [a for a in adapters if a["vehicle"]==vehicle and a["attachment"]==attachment]
    if len(matches)!=1:
        raise ValueError("unsupported or ambiguous vehicle/attachment pair")
    if any(key not in matches[0]["sockets"] for key in required):
        raise ValueError("missing required socket")
    return matches[0]
