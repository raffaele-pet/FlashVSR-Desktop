import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from bridge.worker import geometry, presets, stretch_frames, enable_full_frame_geometry


class GeometryTests(unittest.TestCase):
    def test_all_sixteen_presets_have_exact_output_and_valid_model_canvas(self):
        self.assertEqual(len(presets()), 8)
        for preset in presets():
            a, b = map(int, preset['id'].split(':'))
            for quality in ('standard', 'high'):
                p = geometry(preset['id'], quality)
                with self.subTest(aspect=preset['id'], quality=quality):
                    self.assertEqual(p['width'] * b, p['height'] * a)
                    self.assertEqual(p['width'] % 2, 0)
                    self.assertEqual(p['height'] % 2, 0)
                    self.assertEqual(p['nativeWidth'] % 128, 0)
                    self.assertEqual(p['nativeHeight'] % 128, 0)
                    self.assertLessEqual(p['nativeWidth'] * p['nativeHeight'], 3_900_000)

    def test_resize_keeps_all_four_distinct_borders_instead_of_cropping(self):
        source = np.zeros((1, 90, 160, 3), np.uint8)
        source[:, :12] = (255, 0, 0)
        source[:, -12:] = (0, 255, 0)
        source[:, :, :12] = (0, 0, 255)
        source[:, :, -12:] = (255, 255, 0)
        for p in presets():
            for quality in ('standard', 'high'):
                plan = p[quality]
                # Smaller canvases exercise the same resize primitive quickly.
                canvas = stretch_frames(source, plan['nativeHeight']//8, plan['nativeWidth']//8)
                final = np.asarray(Image.fromarray(canvas[0]).resize((plan['width']//8, plan['height']//8)))
                mid_y, mid_x = final.shape[0]//2, final.shape[1]//2
                for actual, expected in [(final[1,mid_x],(255,0,0)), (final[-2,mid_x],(0,255,0)), (final[mid_y,1],(0,0,255)), (final[mid_y,-2],(255,255,0))]:
                    self.assertLess(np.abs(actual.astype(float)-expected).max(), 8)

    def test_actual_reader_uses_stretch(self):
        from inference.preprocessing import input as reader
        enable_full_frame_geometry(256, 144)
        source=np.zeros((1,72,128,3),np.uint8)
        source[:,:,:12]=(255,0,0);source[:,:,-12:]=(0,255,0)
        resized=reader.pil_center_crop_resize(source,128,128)
        np.testing.assert_array_equal(resized[0,64,1],(255,0,0))
        np.testing.assert_array_equal(resized[0,64,-2],(0,255,0))

    def test_final_writer_restores_exact_ratio_and_frame_count(self):
        import cv2
        import torch
        import inference.pipeline as pipeline
        for aspect in ('16:9','9:16','1:1','21:9'):
            a,b=map(int,aspect.split(':')); w,h=a*16,b*16
            with tempfile.TemporaryDirectory() as folder:
                enable_full_frame_geometry(w,h)
                path=Path(folder)/'video.mp4'
                writer=pipeline.IncrementalVideoWriter(path,24)
                # A native-size mismatch must be corrected before encoding.
                writer.append(torch.zeros(1,3,3,128,128))
                writer.close(); result=writer.verify()
                self.assertEqual((result['width'],result['height'],result['frames']),(w,h,3))


if __name__ == '__main__':
    unittest.main()
