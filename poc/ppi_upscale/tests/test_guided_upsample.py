"""Tests for guided upsampler."""

import tempfile
from pathlib import Path

import numpy as np
import pytest
from PIL import Image
from scipy.ndimage import sobel

from src import PPISimple, GuidedUpsampler


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


@pytest.fixture
def sample_ppi():
    """Create a sample PPI with some structure for testing."""
    img = np.zeros((50, 50), dtype=np.float32)
    img[:, :] = np.linspace(50, 200, 50).reshape(1, -1)
    img[:, 25:] += 50
    return img


@pytest.fixture
def sample_channels():
    """Create sample MSFA channels (3 channels) for testing."""
    channels = np.zeros((3, 50, 50), dtype=np.float32)
    for i in range(3):
        channels[i, :, :] = np.linspace(50 + i * 30, 200 + i * 20, 50).reshape(1, -1)
        channels[i, :, 25:] += 30 + i * 10  # Different edge strengths
    return channels


class TestGuidedUpsamplerInit:
    def test_default_init(self):
        upscaler = GuidedUpsampler()
        assert upscaler.scale_factor == 2
        assert upscaler.method == "guided"

    def test_custom_scale_factor(self):
        upscaler = GuidedUpsampler(scale_factor=4)
        assert upscaler.scale_factor == 4

    def test_custom_method(self):
        upscaler = GuidedUpsampler(method="bicubic")
        assert upscaler.method == "bicubic"

    def test_invalid_scale_factor(self):
        with pytest.raises(ValueError, match="scale_factor must be >= 1"):
            GuidedUpsampler(scale_factor=0)

    def test_invalid_method(self):
        with pytest.raises(ValueError, match="Unknown method"):
            GuidedUpsampler(method="invalid")


class TestBicubicUpscale:
    def test_output_shape(self, sample_ppi):
        upscaler = GuidedUpsampler(scale_factor=2, method="bicubic")
        result = upscaler.upscale(sample_ppi)

        assert result.shape == (100, 100)

    def test_scale_factor_4(self, sample_ppi):
        upscaler = GuidedUpsampler(scale_factor=4, method="bicubic")
        result = upscaler.upscale(sample_ppi)

        assert result.shape == (200, 200)

    def test_preserves_value_range(self, sample_ppi):
        upscaler = GuidedUpsampler(scale_factor=2, method="bicubic")
        result = upscaler.upscale(sample_ppi)

        assert result.min() >= sample_ppi.min() - 20
        assert result.max() <= sample_ppi.max() + 20


class TestLanczosUpscale:
    def test_output_shape(self, sample_ppi):
        upscaler = GuidedUpsampler(scale_factor=2, method="lanczos")
        result = upscaler.upscale(sample_ppi)

        assert result.shape == (100, 100)

    def test_preserves_value_range(self, sample_ppi):
        upscaler = GuidedUpsampler(scale_factor=2, method="lanczos")
        result = upscaler.upscale(sample_ppi)

        assert result.min() >= sample_ppi.min() - 30
        assert result.max() <= sample_ppi.max() + 30


class TestGuidedUpscale:
    def test_output_shape_without_channels(self, sample_ppi):
        """Test guided upscale falls back to edge guide when no channels."""
        upscaler = GuidedUpsampler(scale_factor=2, method="guided")
        result = upscaler.upscale(sample_ppi)

        assert result.shape == (100, 100)

    def test_output_shape_with_channels(self, sample_ppi, sample_channels):
        """Test guided upscale with MSFA channels."""
        upscaler = GuidedUpsampler(scale_factor=2, method="guided")
        result = upscaler.upscale(sample_ppi, channels=sample_channels)

        assert result.shape == (100, 100)

    def test_preserves_value_range(self, sample_ppi, sample_channels):
        upscaler = GuidedUpsampler(scale_factor=2, method="guided")
        result = upscaler.upscale(sample_ppi, channels=sample_channels)

        assert result.min() >= 0
        assert result.max() <= 300


class TestMSFAGuide:
    def test_msfa_guide_shape(self, sample_channels):
        upscaler = GuidedUpsampler()
        guide = upscaler._compute_msfa_guide(sample_channels)

        assert guide.shape == sample_channels.shape[1:]

    def test_msfa_guide_normalized(self, sample_channels):
        upscaler = GuidedUpsampler()
        guide = upscaler._compute_msfa_guide(sample_channels)

        assert guide.min() >= 0.0
        assert guide.max() <= 1.0

    def test_msfa_guide_detects_edges(self, sample_channels):
        upscaler = GuidedUpsampler()
        guide = upscaler._compute_msfa_guide(sample_channels)

        # The edge at column 25 should have higher values
        edge_region = guide[:, 23:27].mean()
        non_edge_region = guide[:, :10].mean()

        assert edge_region > non_edge_region

    def test_msfa_guide_uses_all_channels(self):
        """Test that MSFA guide combines info from all channels."""
        upscaler = GuidedUpsampler()

        # Create channels with different edge locations
        channels = np.zeros((3, 50, 50), dtype=np.float32)
        channels[0, :, 10:] = 100  # Edge at col 10
        channels[1, :, 25:] = 100  # Edge at col 25
        channels[2, :, 40:] = 100  # Edge at col 40

        guide = upscaler._compute_msfa_guide(channels)

        # All three edges should be detected
        assert guide[:, 8:12].mean() > guide[:, :5].mean()
        assert guide[:, 23:27].mean() > guide[:, 15:20].mean()
        assert guide[:, 38:42].mean() > guide[:, 30:35].mean()


class TestEdgePreservation:
    def test_guided_with_channels_preserves_edges(self, sample_ppi, sample_channels):
        """Guided upscale with MSFA channels should preserve edges."""
        upscaler_bicubic = GuidedUpsampler(scale_factor=2, method="bicubic")
        upscaler_guided = GuidedUpsampler(scale_factor=2, method="guided")

        ppi_bicubic = upscaler_bicubic.upscale(sample_ppi)
        ppi_guided = upscaler_guided.upscale(sample_ppi, channels=sample_channels)

        def edge_strength(img):
            edge_x = sobel(img, axis=1)
            edge_y = sobel(img, axis=0)
            return np.sqrt(edge_x**2 + edge_y**2).mean()

        edge_bicubic = edge_strength(ppi_bicubic)
        edge_guided = edge_strength(ppi_guided)

        assert edge_guided >= edge_bicubic * 0.5


class TestComputeEdgeGuide:
    def test_edge_guide_shape(self, sample_ppi):
        upscaler = GuidedUpsampler()
        guide = upscaler._compute_edge_guide(sample_ppi)

        assert guide.shape == sample_ppi.shape

    def test_edge_guide_normalized(self, sample_ppi):
        upscaler = GuidedUpsampler()
        guide = upscaler._compute_edge_guide(sample_ppi)

        assert guide.min() >= 0.0
        assert guide.max() <= 1.0

    def test_edge_guide_detects_edges(self, sample_ppi):
        upscaler = GuidedUpsampler()
        guide = upscaler._compute_edge_guide(sample_ppi)

        edge_region = guide[:, 23:27].mean()
        non_edge_region = guide[:, :10].mean()

        assert edge_region > non_edge_region


class TestGuidedFilter:
    def test_guided_filter_output_shape(self, sample_ppi):
        upscaler = GuidedUpsampler()
        guide = sample_ppi / 255.0
        result = upscaler._apply_guided_filter(sample_ppi, guide, radius=2, eps=0.01)

        assert result.shape == sample_ppi.shape

    def test_guided_filter_smooths(self, sample_ppi):
        upscaler = GuidedUpsampler()
        noisy = sample_ppi + np.random.randn(*sample_ppi.shape) * 10
        guide = sample_ppi / 255.0

        result = upscaler._apply_guided_filter(noisy, guide, radius=4, eps=0.1)

        assert result.std() < noisy.std()


class TestGetOutputSize:
    def test_output_size_calculation(self):
        upscaler = GuidedUpsampler(scale_factor=2)
        assert upscaler.get_output_size((100, 200)) == (200, 400)

    def test_output_size_scale_4(self):
        upscaler = GuidedUpsampler(scale_factor=4)
        assert upscaler.get_output_size((100, 200)) == (400, 800)


class TestWithRealData:
    def test_upscale_real_ppi_bicubic(self, real_data_dir):
        generator = PPISimple(real_data_dir)
        ppi = generator.generate_ppi()

        upscaler = GuidedUpsampler(scale_factor=2, method="bicubic")
        result = upscaler.upscale(ppi)

        assert result.shape == (2024, 2128)

    def test_upscale_real_ppi_guided_with_channels(self, real_data_dir):
        """Test guided upscale with real MSFA channels."""
        generator = PPISimple(real_data_dir)
        channels = generator.load_channels()
        ppi = generator.generate_ppi()

        upscaler = GuidedUpsampler(scale_factor=2, method="guided")
        result = upscaler.upscale(ppi, channels=channels)

        assert result.shape == (2024, 2128)

    def test_upscale_real_ppi_guided_fallback(self, real_data_dir):
        """Test guided upscale falls back when no channels provided."""
        generator = PPISimple(real_data_dir)
        ppi = generator.generate_ppi()

        upscaler = GuidedUpsampler(scale_factor=2, method="guided")
        result = upscaler.upscale(ppi)  # No channels

        assert result.shape == (2024, 2128)

    def test_upscale_real_ppi_lanczos(self, real_data_dir):
        generator = PPISimple(real_data_dir)
        ppi = generator.generate_ppi()

        upscaler = GuidedUpsampler(scale_factor=2, method="lanczos")
        result = upscaler.upscale(ppi)

        assert result.shape == (2024, 2128)

    def test_edge_comparison_with_channels(self, real_data_dir):
        """Compare edge preservation: bicubic vs guided with MSFA channels."""
        generator = PPISimple(real_data_dir)
        channels = generator.load_channels()
        ppi = generator.generate_ppi()

        upscaler_bicubic = GuidedUpsampler(scale_factor=2, method="bicubic")
        upscaler_guided = GuidedUpsampler(scale_factor=2, method="guided")

        ppi_bicubic = upscaler_bicubic.upscale(ppi)
        ppi_guided = upscaler_guided.upscale(ppi, channels=channels)

        def edge_strength(img):
            edge_x = sobel(img, axis=1)
            edge_y = sobel(img, axis=0)
            return np.sqrt(edge_x**2 + edge_y**2).mean()

        edge_orig = edge_strength(ppi)
        edge_bicubic = edge_strength(ppi_bicubic)
        edge_guided = edge_strength(ppi_guided)

        print(f"Original edge: {edge_orig:.4f}")
        print(f"Bicubic edge:  {edge_bicubic:.4f}")
        print(f"Guided (MSFA) edge: {edge_guided:.4f}")

        assert edge_bicubic > 0
        assert edge_guided > 0
