"""Tests for PPI generators."""

import tempfile
from pathlib import Path

import numpy as np
import pytest
from PIL import Image

from src import PPISimple, PPIPPID, PPIIGFPPI


@pytest.fixture
def temp_channel_dir():
    """Create temporary directory with test channel images."""
    with tempfile.TemporaryDirectory() as tmpdir:
        tmpdir = Path(tmpdir)
        # Create 3 test channels with known values
        for i, wavelength in enumerate([410, 430, 450]):
            value = 100 + i * 50  # 100, 150, 200
            arr = np.full((100, 100), value, dtype=np.uint8)
            img = Image.fromarray(arr, mode="L")
            img.save(tmpdir / f"{wavelength}nm.png")
        yield tmpdir


@pytest.fixture
def real_data_dir():
    """Path to real data directory."""
    data_dir = Path(__file__).parent.parent / "data"
    if not data_dir.exists():
        pytest.skip("Real data directory not found")
    return data_dir


class TestPPISimple:
    def test_load_channels(self, temp_channel_dir):
        generator = PPISimple(temp_channel_dir)
        channels = generator.load_channels()

        assert channels.shape == (3, 100, 100)
        assert channels.dtype == np.float32

    def test_load_channels_no_files(self, tmp_path):
        generator = PPISimple(tmp_path)

        with pytest.raises(FileNotFoundError):
            generator.load_channels()

    def test_generate_ppi(self, temp_channel_dir):
        generator = PPISimple(temp_channel_dir)
        ppi = generator.generate_ppi()

        # Average of 100, 150, 200 = 150
        assert ppi.shape == (100, 100)
        assert np.allclose(ppi.mean(), 150.0)

    def test_save_ppi(self, temp_channel_dir, tmp_path):
        generator = PPISimple(temp_channel_dir)
        output_path = tmp_path / "output" / "test_ppi.png"

        generator.generate_ppi()
        saved_path = generator.save_ppi(output_path)

        assert saved_path.exists()
        img = Image.open(saved_path)
        assert img.mode == "L"
        assert img.size == (100, 100)

    def test_get_statistics(self, temp_channel_dir):
        generator = PPISimple(temp_channel_dir)
        generator.generate_ppi()
        stats = generator.get_statistics()

        assert stats["method"] == "simple"
        assert stats["num_channels"] == 3
        assert stats["mean"] == 150.0

    def test_method_name(self, temp_channel_dir):
        generator = PPISimple(temp_channel_dir)
        assert generator.method_name == "simple"


class TestPPIPPID:
    def test_generate_ppi(self, temp_channel_dir):
        generator = PPIPPID(temp_channel_dir)
        ppi = generator.generate_ppi()

        assert ppi.shape == (100, 100)
        # PPID should be close to simple average for uniform images
        assert np.allclose(ppi.mean(), 150.0, atol=1.0)

    def test_method_name(self, temp_channel_dir):
        generator = PPIPPID(temp_channel_dir)
        assert generator.method_name == "ppid"

    def test_inherits_from_simple(self, temp_channel_dir):
        generator = PPIPPID(temp_channel_dir)
        assert isinstance(generator, PPISimple)


class TestPPIIGFPPI:
    def test_generate_ppi(self, temp_channel_dir):
        generator = PPIIGFPPI(temp_channel_dir)
        ppi = generator.generate_ppi()

        assert ppi.shape == (100, 100)
        # IGFPPI should be close to simple average for uniform images
        assert np.allclose(ppi.mean(), 150.0, atol=1.0)

    def test_method_name(self, temp_channel_dir):
        generator = PPIIGFPPI(temp_channel_dir)
        assert generator.method_name == "igfppi"

    def test_inherits_from_simple(self, temp_channel_dir):
        generator = PPIIGFPPI(temp_channel_dir)
        assert isinstance(generator, PPISimple)

    def test_statistics_include_iterations(self, temp_channel_dir):
        generator = PPIIGFPPI(temp_channel_dir)
        generator.generate_ppi()
        stats = generator.get_statistics()

        assert "iterations_horizontal" in stats
        assert "iterations_vertical" in stats
        assert stats["iterations_horizontal"] >= 1
        assert stats["iterations_vertical"] >= 1

    def test_custom_parameters(self, temp_channel_dir):
        generator = PPIIGFPPI(
            temp_channel_dir,
            epsilon_pixel=1e-5,
            epsilon_global=1e-4,
            max_iterations=100,
        )
        ppi = generator.generate_ppi()
        assert ppi.shape == (100, 100)


class TestWithRealData:
    def test_simple_with_real_data(self, real_data_dir):
        generator = PPISimple(real_data_dir)
        channels = generator.load_channels()

        assert channels.shape[0] == 15
        assert channels.shape[1:] == (1012, 1064)

        ppi = generator.generate_ppi()
        assert ppi.shape == (1012, 1064)

        stats = generator.get_statistics()
        assert 0 <= stats["mean"] <= 255

    def test_ppid_with_real_data(self, real_data_dir):
        generator = PPIPPID(real_data_dir)
        ppi = generator.generate_ppi()

        assert ppi.shape == (1012, 1064)
        stats = generator.get_statistics()
        assert stats["method"] == "ppid"

    def test_igfppi_with_real_data(self, real_data_dir):
        generator = PPIIGFPPI(real_data_dir)
        ppi = generator.generate_ppi()

        assert ppi.shape == (1012, 1064)
        stats = generator.get_statistics()
        assert stats["method"] == "igfppi"
        assert stats["iterations_horizontal"] >= 1
        assert stats["iterations_vertical"] >= 1
