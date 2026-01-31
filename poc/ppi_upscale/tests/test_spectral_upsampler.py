"""Tests for spectral channel upsampler."""

import numpy as np
import pytest

from src.spectral_difference import compute_spectral_difference, compute_all_spectral_differences
from src.btes_upsample import btes_upsample
from src.spectral_reconstruct import reconstruct_channel, reconstruct_all_channels
from src.spectral_upsampler import SpectralUpsampler


class TestSpectralDifference:
    """Tests for spectral difference computation."""

    def test_compute_spectral_difference_basic(self):
        """Test basic spectral difference computation."""
        channel = np.array([[10, 20], [30, 40]], dtype=np.float32)
        ppi = np.array([[5, 10], [15, 20]], dtype=np.float32)

        delta = compute_spectral_difference(channel, ppi)

        expected = np.array([[5, 10], [15, 20]], dtype=np.float32)
        np.testing.assert_array_equal(delta, expected)

    def test_compute_spectral_difference_negative(self):
        """Test spectral difference with negative values."""
        channel = np.array([[5, 10], [15, 20]], dtype=np.float32)
        ppi = np.array([[10, 20], [30, 40]], dtype=np.float32)

        delta = compute_spectral_difference(channel, ppi)

        expected = np.array([[-5, -10], [-15, -20]], dtype=np.float32)
        np.testing.assert_array_equal(delta, expected)

    def test_compute_all_spectral_differences(self):
        """Test computing all spectral differences at once."""
        channels = np.array([
            [[10, 20], [30, 40]],
            [[15, 25], [35, 45]],
        ], dtype=np.float32)
        ppi = np.array([[5, 10], [15, 20]], dtype=np.float32)

        deltas = compute_all_spectral_differences(channels, ppi)

        assert deltas.shape == (2, 2, 2)
        expected_0 = np.array([[5, 10], [15, 20]], dtype=np.float32)
        expected_1 = np.array([[10, 15], [20, 25]], dtype=np.float32)
        np.testing.assert_array_equal(deltas[0], expected_0)
        np.testing.assert_array_equal(deltas[1], expected_1)


class TestBTESUpsample:
    """Tests for BTES upsampling."""

    def test_btes_upsample_output_shape(self):
        """Test that BTES upsample produces correct output shape."""
        H, W = 4, 6
        delta = np.random.rand(H, W).astype(np.float32)
        ppi_2x = np.random.rand(H * 2, W * 2).astype(np.float32)

        delta_2x = btes_upsample(delta, ppi_2x)

        assert delta_2x.shape == (H * 2, W * 2)

    def test_btes_upsample_preserves_original(self):
        """Test that original values are preserved at even,even positions."""
        H, W = 4, 6
        delta = np.random.rand(H, W).astype(np.float32)
        ppi_2x = np.random.rand(H * 2, W * 2).astype(np.float32)

        delta_2x = btes_upsample(delta, ppi_2x)

        # Check even,even positions
        np.testing.assert_array_almost_equal(delta_2x[0::2, 0::2], delta)

    def test_btes_upsample_uniform_delta(self):
        """Test BTES upsample with uniform delta - should produce uniform result."""
        H, W = 4, 4
        delta = np.ones((H, W), dtype=np.float32) * 5.0
        ppi_2x = np.random.rand(H * 2, W * 2).astype(np.float32)

        delta_2x = btes_upsample(delta, ppi_2x)

        # All interpolated values should be close to 5.0
        np.testing.assert_array_almost_equal(delta_2x, 5.0, decimal=5)

    def test_btes_upsample_gradient_aware(self):
        """Test that BTES respects edge structure from PPI."""
        H, W = 4, 4
        # Create delta with a gradient
        delta = np.array([
            [0, 0, 10, 10],
            [0, 0, 10, 10],
            [0, 0, 10, 10],
            [0, 0, 10, 10],
        ], dtype=np.float32)

        # Create PPI_2x with a vertical edge matching delta structure
        ppi_2x = np.zeros((H * 2, W * 2), dtype=np.float32)
        ppi_2x[:, 4:] = 100.0  # Vertical edge at column 4

        delta_2x = btes_upsample(delta, ppi_2x)

        # Check that the edge is preserved (not blurred)
        # Left side should be close to 0, right side close to 10
        assert delta_2x[:, 0:3].mean() < 2.0
        assert delta_2x[:, 5:].mean() > 8.0


class TestSpectralReconstruct:
    """Tests for spectral reconstruction."""

    def test_reconstruct_channel_basic(self):
        """Test basic channel reconstruction."""
        ppi_2x = np.array([[10, 20], [30, 40]], dtype=np.float32)
        delta_2x = np.array([[5, 10], [15, 20]], dtype=np.float32)

        result = reconstruct_channel(ppi_2x, delta_2x)

        expected = np.array([[15, 30], [45, 60]], dtype=np.float32)
        np.testing.assert_array_equal(result, expected)

    def test_reconstruct_all_channels(self):
        """Test reconstructing all channels at once."""
        ppi_2x = np.array([[10, 20], [30, 40]], dtype=np.float32)
        deltas_2x = np.array([
            [[5, 10], [15, 20]],
            [[-5, -10], [-15, -20]],
        ], dtype=np.float32)

        result = reconstruct_all_channels(ppi_2x, deltas_2x)

        assert result.shape == (2, 2, 2)
        expected_0 = np.array([[15, 30], [45, 60]], dtype=np.float32)
        expected_1 = np.array([[5, 10], [15, 20]], dtype=np.float32)
        np.testing.assert_array_equal(result[0], expected_0)
        np.testing.assert_array_equal(result[1], expected_1)


class TestSpectralUpsampler:
    """Tests for the SpectralUpsampler class."""

    def test_init_valid_scale_factor(self):
        """Test initialization with valid scale factor."""
        upsampler = SpectralUpsampler(scale_factor=2)
        assert upsampler.scale_factor == 2

    def test_init_invalid_scale_factor(self):
        """Test initialization with invalid scale factor raises error."""
        with pytest.raises(ValueError, match="only 2× upsampling"):
            SpectralUpsampler(scale_factor=4)

    def test_upsample_channel_output_shape(self):
        """Test that upsample_channel produces correct output shape."""
        H, W = 4, 6
        channel = np.random.rand(H, W).astype(np.float32) * 100
        ppi = np.random.rand(H, W).astype(np.float32) * 100
        ppi_2x = np.random.rand(H * 2, W * 2).astype(np.float32) * 100

        upsampler = SpectralUpsampler()
        result = upsampler.upsample_channel(channel, ppi, ppi_2x)

        assert result.shape == (H * 2, W * 2)

    def test_upsample_all_channels_output_shape(self):
        """Test that upsample_all_channels produces correct output shape."""
        N, H, W = 15, 4, 6
        channels = np.random.rand(N, H, W).astype(np.float32) * 100
        ppi = np.random.rand(H, W).astype(np.float32) * 100
        ppi_2x = np.random.rand(H * 2, W * 2).astype(np.float32) * 100

        upsampler = SpectralUpsampler()
        result = upsampler.upsample_all_channels(channels, ppi, ppi_2x)

        assert result.shape == (N, H * 2, W * 2)

    def test_upsample_preserves_spectral_difference(self):
        """Test that upsampling preserves spectral characteristics."""
        H, W = 4, 4
        # Channel with known spectral difference from PPI
        ppi = np.ones((H, W), dtype=np.float32) * 50
        channel = np.ones((H, W), dtype=np.float32) * 60  # delta = 10

        # Create ppi_2x (uniform for simplicity)
        ppi_2x = np.ones((H * 2, W * 2), dtype=np.float32) * 50

        upsampler = SpectralUpsampler()
        result = upsampler.upsample_channel(channel, ppi, ppi_2x)

        # Result should be approximately ppi_2x + 10 = 60
        np.testing.assert_array_almost_equal(result, 60.0, decimal=5)

    def test_upsample_channel_consistency(self):
        """Test that single channel and all channels methods give same result."""
        H, W = 4, 6
        channels = np.random.rand(3, H, W).astype(np.float32) * 100
        ppi = np.random.rand(H, W).astype(np.float32) * 100
        ppi_2x = np.random.rand(H * 2, W * 2).astype(np.float32) * 100

        upsampler = SpectralUpsampler()

        # Single channel method
        single_results = [
            upsampler.upsample_channel(channels[i], ppi, ppi_2x)
            for i in range(3)
        ]

        # All channels method
        all_results = upsampler.upsample_all_channels(channels, ppi, ppi_2x)

        for i in range(3):
            np.testing.assert_array_almost_equal(single_results[i], all_results[i])


class TestEdgeCases:
    """Test edge cases and boundary conditions."""

    def test_small_image(self):
        """Test with minimal image size."""
        H, W = 2, 2
        channel = np.array([[10, 20], [30, 40]], dtype=np.float32)
        ppi = np.array([[5, 10], [15, 20]], dtype=np.float32)
        ppi_2x = np.array([
            [5, 7, 10, 12],
            [10, 12, 15, 17],
            [15, 17, 20, 22],
            [20, 22, 25, 27],
        ], dtype=np.float32)

        upsampler = SpectralUpsampler()
        result = upsampler.upsample_channel(channel, ppi, ppi_2x)

        assert result.shape == (4, 4)
        # Original positions should be preserved
        assert result[0, 0] == pytest.approx(ppi_2x[0, 0] + 5, rel=1e-5)
        assert result[0, 2] == pytest.approx(ppi_2x[0, 2] + 10, rel=1e-5)
        assert result[2, 0] == pytest.approx(ppi_2x[2, 0] + 15, rel=1e-5)
        assert result[2, 2] == pytest.approx(ppi_2x[2, 2] + 20, rel=1e-5)

    def test_zero_delta(self):
        """Test when channel equals PPI (delta = 0)."""
        H, W = 4, 4
        ppi = np.random.rand(H, W).astype(np.float32) * 100
        channel = ppi.copy()  # delta = 0
        ppi_2x = np.random.rand(H * 2, W * 2).astype(np.float32) * 100

        upsampler = SpectralUpsampler()
        result = upsampler.upsample_channel(channel, ppi, ppi_2x)

        # Result should equal ppi_2x since delta is 0
        np.testing.assert_array_almost_equal(result, ppi_2x, decimal=5)

    def test_negative_values(self):
        """Test with negative pixel values."""
        H, W = 4, 4
        channel = np.random.rand(H, W).astype(np.float32) * 50 - 25  # -25 to 25
        ppi = np.random.rand(H, W).astype(np.float32) * 50 - 25
        ppi_2x = np.random.rand(H * 2, W * 2).astype(np.float32) * 50 - 25

        upsampler = SpectralUpsampler()
        result = upsampler.upsample_channel(channel, ppi, ppi_2x)

        assert result.shape == (H * 2, W * 2)
        # No NaN or inf values
        assert np.all(np.isfinite(result))


class TestIntegration:
    """Integration tests for the full pipeline."""

    def test_full_pipeline_15_channels(self):
        """Test the full pipeline with 15 channels."""
        H, W = 10, 12
        N = 15
        channels = np.random.rand(N, H, W).astype(np.float32) * 255
        ppi = channels.mean(axis=0)  # Approximate PPI
        ppi_2x = np.zeros((H * 2, W * 2), dtype=np.float32)
        # Simple bilinear for ppi_2x approximation
        for i in range(H):
            for j in range(W):
                ppi_2x[2*i, 2*j] = ppi[i, j]
                if j < W - 1:
                    ppi_2x[2*i, 2*j + 1] = (ppi[i, j] + ppi[i, j + 1]) / 2
                if i < H - 1:
                    ppi_2x[2*i + 1, 2*j] = (ppi[i, j] + ppi[i + 1, j]) / 2
                if i < H - 1 and j < W - 1:
                    ppi_2x[2*i + 1, 2*j + 1] = (
                        ppi[i, j] + ppi[i, j + 1] + ppi[i + 1, j] + ppi[i + 1, j + 1]
                    ) / 4
        # Fill edge cases
        ppi_2x[::2, -1] = ppi[:, -1]
        ppi_2x[-1, ::2] = ppi[-1, :]
        ppi_2x[-1, -1] = ppi[-1, -1]

        upsampler = SpectralUpsampler()
        result = upsampler.upsample_all_channels(channels, ppi, ppi_2x)

        assert result.shape == (N, H * 2, W * 2)
        assert np.all(np.isfinite(result))

        # Check that mean is roughly preserved
        for i in range(N):
            orig_mean = channels[i].mean()
            result_mean = result[i].mean()
            # Allow some deviation due to interpolation
            assert abs(result_mean - orig_mean) < 30, f"Channel {i} mean changed too much"
