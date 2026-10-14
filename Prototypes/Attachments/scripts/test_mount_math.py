"""Small numerical tests; run with Blender's bundled Python/NumPy."""
import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import numpy as np
import mount_math as mm

LIMITS = {"strained_m": .025, "invalid_m": .06, "spacing_fraction": .25}

class MountTests(unittest.TestCase):
    def test_known_barycentric_anchor_follows_moving_triangle(self):
        verts = np.array([[0.,0,0],[1,0,0],[0,1,0]])
        tris = [[0,1,2]]
        rev = mm.mesh_revision(verts, tris)
        b = mm.bind_anchors(verts, tris, [{"id":"test", "origin":[.2,.3,1.],
             "direction":[0,0,-1], "max_distance":2.,
             "bounds":[[-.1,-.1,-.1],[1.1,1.1,.1]]}])[0]
        np.testing.assert_allclose(b["weights"], [.5,.2,.3], atol=1e-7)
        moved = verts + [.4,-.2,.7]
        point, normal = mm.sample_anchor(moved, b, rev)
        np.testing.assert_allclose(point, [.6,.1,.7], atol=1e-7)
        np.testing.assert_allclose(normal, [0,0,1], atol=1e-7)

    def test_rigid_fit_recovers_translation_rotation_without_scale(self):
        p = np.array([[-1,-1,0],[1,-1,0],[-1,1,0],[1,1,0.]])
        a = .37
        r = np.array([[1,0,0],[0,np.cos(a),-np.sin(a)],[0,np.sin(a),np.cos(a)]])
        q = p @ r.T + [.1,.2,-.3]
        pose = mm.fit_rigid(p,q)
        np.testing.assert_allclose(mm.transform(pose,p), q, atol=1e-9)
        self.assertAlmostEqual(np.linalg.det(pose[:3,:3]), 1.)
        np.testing.assert_allclose(pose[:3,:3].T @ pose[:3,:3], np.eye(3), atol=1e-9)

    def test_warped_support_reports_invalid_rigid_mount(self):
        p = np.array([[-.21,-.25,0],[.21,-.25,0],[-.21,.25,0],[.21,.25,0.]])
        q = p.copy()
        q[3,2] += .40
        result = mm.assess(p,q,mm.fit_rigid(p,q),p,LIMITS)
        self.assertEqual(result["status"], "invalid_support")
        self.assertGreater(result["max_residual_m"], .06)

    def test_stale_binding_and_invalid_index_are_rejected(self):
        v = [[0,0,0],[1,0,0],[0,1,0]]
        b = mm.bind_anchors(v,[[0,1,2]],[{"id":"t","origin":[.2,.3,1],
             "direction":[0,0,-1],"max_distance":2,"bounds":[[-1,-1,-1],[2,2,2]]}])[0]
        with self.assertRaisesRegex(ValueError,"revision"):
            mm.sample_anchor(v,b,"changed topology")
        with self.assertRaisesRegex(ValueError,"index"):
            mm.sample_anchor(v[:2],b,b["revision"])

    def test_collapsed_triangle_and_collinear_fit_are_rejected(self):
        v = [[0,0,0],[1,0,0],[0,1,0]]
        b = mm.bind_anchors(v,[[0,1,2]],[{"id":"t","origin":[.2,.3,1],
             "direction":[0,0,-1],"max_distance":2,"bounds":[[-1,-1,-1],[2,2,2]]}])[0]
        with self.assertRaisesRegex(ValueError,"collapsed"):
            mm.sample_anchor([[0,0,0],[1,0,0],[2,0,0]],b,b["revision"])
        with self.assertRaisesRegex(ValueError,"collinear"):
            mm.fit_rigid([[0,0,0],[1,0,0],[2,0,0]], [[0,0,0],[1,0,0],[2,0,0]])

    def test_nonfinite_input_and_missing_surface_are_rejected(self):
        with self.assertRaisesRegex(ValueError,"finite"):
            mm.fit_rigid([[0,0,0],[1,0,0],[0,1,0]], [[0,0,0],[1,0,0],[0,1,float("nan")]])
        with self.assertRaisesRegex(ValueError,"surface"):
            mm.bind_anchors([[0,0,0],[1,0,0],[0,1,0]],[[0,1,2]],
                [{"id":"miss","origin":[2,2,1],"direction":[0,0,-1],
                  "max_distance":2,"bounds":[[-1,-1,-1],[3,3,3]]}])

    def test_unknown_pair_and_missing_socket_are_rejected(self):
        adapters = [{"vehicle":"hatch","attachment":"scoop","sockets":{"a":[0,0,0]}}]
        with self.assertRaisesRegex(ValueError,"unsupported"):
            mm.select_adapter(adapters,"pickup","scoop",["a"])
        with self.assertRaisesRegex(ValueError,"socket"):
            mm.select_adapter(adapters,"hatch","scoop",["missing"])

if __name__ == "__main__":
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(MountTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    if not result.wasSuccessful():
        raise SystemExit(1)
