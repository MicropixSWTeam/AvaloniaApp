"""Guided upsampling for edge-aware super resolution.

Implements upscaling with edge preservation using guided filter approach.
Based on the guided upsampling principle from the IGFPPI paper (Eq.17).

Uses raw MSFA channels as guide source for better edge/smoothing information.
"""

import numpy as np
from scipy.ndimage import sobel, uniform_filter, zoom


class GuidedUpsampler:
    """Image upsampler with guided filtering using raw MSFA channels."""

    def __init__(self, scale_factor: int = 2, method: str = "guided"):
        """Initialize upsampler.

        Args:
            scale_factor: Upscaling factor (default: 2)
            method: Upscaling method - "guided", "bicubic", or "lanczos"
        """
        if scale_factor < 1:
            raise ValueError("scale_factor must be >= 1")
        if method not in ("guided", "bicubic", "lanczos"):
            raise ValueError(f"Unknown method: {method}")

        self.scale_factor = scale_factor
        self.method = method

    def upscale(
        self, img: np.ndarray, channels: np.ndarray = None
    ) -> np.ndarray:
        """Upscale image using raw MSFA channels as guide.

        Args:
            img: Input image (H, W), float32
            channels: Raw MSFA channels (N, H, W) for guided upscaling.
                      If None, falls back to edge-based guide.

        Returns:
            Upscaled image (H*scale, W*scale), float32
        """
        if self.method == "guided":
            return self._guided_upscale(img, channels)
        elif self.method == "bicubic":
            return self._bicubic_upscale(img)
        else:  # lanczos
            return self._lanczos_upscale(img)

    def _bicubic_upscale(self, img: np.ndarray) -> np.ndarray:
        """Upscale using bicubic interpolation."""
        return zoom(img, self.scale_factor, order=3)

    def _lanczos_upscale(self, img: np.ndarray) -> np.ndarray:
        """Upscale using Lanczos interpolation (approximated via high-order spline)."""
        return zoom(img, self.scale_factor, order=5)

    def _guided_upscale(
        self, img: np.ndarray, channels: np.ndarray = None
    ) -> np.ndarray:
        """Upscale using guided filtering with raw MSFA channels.

        Algorithm:
        1. Initial bicubic upscale of PPI
        2. Compute guide from raw MSFA channels (edge + smoothing info)
        3. Upscale guide
        4. Apply guided filter for edge-aware refinement

        Args:
            img: Input PPI image (H, W)
            channels: Raw MSFA channels (N, H, W). If None, uses PPI edge.

        Returns:
            Upscaled image with preserved edges (H*scale, W*scale)
        """
        # Step 1: Initial bicubic upscale
        img_up = self._bicubic_upscale(img)

        # Step 2: Compute guide from MSFA channels
        if channels is not None:
            guide = self._compute_msfa_guide(channels)
        else:
            # Fallback: use PPI edge magnitude
            guide = self._compute_edge_guide(img)

        # Step 3: Upscale guide
        guide_up = zoom(guide, self.scale_factor, order=3)

        # Step 4: Apply guided filter
        result = self._apply_guided_filter(img_up, guide_up, radius=4, eps=1e-2)

        return result

    def _compute_msfa_guide(self, channels: np.ndarray) -> np.ndarray:
        """Compute guide image from raw MSFA channels.

        Combines edge and smoothing information from all channels.

        Args:
            channels: Raw MSFA channels (N, H, W)

        Returns:
            Guide image (H, W) with edge and structure information
        """
        n_channels = channels.shape[0]

        # Method: Weighted combination of channel edges and mean
        # 1. Compute edge magnitude for each channel
        edge_sum = np.zeros(channels.shape[1:], dtype=np.float32)
        for i in range(n_channels):
            ch = channels[i]
            edge_x = sobel(ch, axis=1)
            edge_y = sobel(ch, axis=0)
            edge_sum += np.sqrt(edge_x**2 + edge_y**2)

        # Average edge across channels
        edge_avg = edge_sum / n_channels

        # 2. Channel mean (smoothing/structure info)
        channel_mean = channels.mean(axis=0)

        # 3. Combine: normalize and blend
        # Edge normalized to [0, 1]
        edge_max = edge_avg.max()
        if edge_max > 0:
            edge_norm = edge_avg / edge_max
        else:
            edge_norm = edge_avg

        # Channel mean normalized to [0, 1]
        mean_min, mean_max = channel_mean.min(), channel_mean.max()
        if mean_max > mean_min:
            mean_norm = (channel_mean - mean_min) / (mean_max - mean_min)
        else:
            mean_norm = np.zeros_like(channel_mean)

        # Blend: edge-weighted structure
        # Higher edge values indicate boundaries to preserve
        guide = mean_norm * (1 + edge_norm)

        # Normalize final guide to [0, 1]
        guide_max = guide.max()
        if guide_max > 0:
            guide = guide / guide_max

        return guide

    def _compute_edge_guide(self, img: np.ndarray) -> np.ndarray:
        """Compute edge magnitude as fallback guide image.

        Args:
            img: Input image (H, W)

        Returns:
            Edge magnitude image (H, W), normalized to [0, 1]
        """
        edge_x = sobel(img, axis=1)
        edge_y = sobel(img, axis=0)
        edge_magnitude = np.sqrt(edge_x**2 + edge_y**2)

        max_val = edge_magnitude.max()
        if max_val > 0:
            edge_magnitude = edge_magnitude / max_val

        return edge_magnitude

    def _apply_guided_filter(
        self,
        img: np.ndarray,
        guide: np.ndarray,
        radius: int = 4,
        eps: float = 1e-2,
    ) -> np.ndarray:
        """Apply guided filter for edge-aware smoothing.

        Implementation of guided filter from:
        "Guided Image Filtering" (He et al., ECCV 2010)

        q_i = a_k * I_i + b_k

        where:
            a_k = cov(I, p) / (var(I) + eps)
            b_k = mean(p) - a_k * mean(I)

        Args:
            img: Input image to filter (p)
            guide: Guide image (I)
            radius: Window radius
            eps: Regularization parameter

        Returns:
            Filtered image
        """
        size = 2 * radius + 1

        mean_I = uniform_filter(guide, size=size, mode="reflect")
        mean_p = uniform_filter(img, size=size, mode="reflect")
        mean_Ip = uniform_filter(guide * img, size=size, mode="reflect")
        mean_II = uniform_filter(guide * guide, size=size, mode="reflect")

        cov_Ip = mean_Ip - mean_I * mean_p
        var_I = mean_II - mean_I * mean_I

        a = cov_Ip / (var_I + eps)
        b = mean_p - a * mean_I

        mean_a = uniform_filter(a, size=size, mode="reflect")
        mean_b = uniform_filter(b, size=size, mode="reflect")

        return mean_a * guide + mean_b

    def get_output_size(self, input_shape: tuple) -> tuple:
        """Calculate output size for given input shape."""
        return (
            input_shape[0] * self.scale_factor,
            input_shape[1] * self.scale_factor,
        )
