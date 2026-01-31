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
        """Upscale using directional interpolation with MSFA guide (Eq. 17-21).

        Algorithm:
        1. Place original pixels at (0::2, 0::2) positions
        2. Compute guide from MSFA channels for weight calculation
        3. Directional interpolation: diagonal → horizontal → vertical
        4. Apply guided filter for refinement

        Args:
            img: Input PPI image (H, W)
            channels: Raw MSFA channels (N, H, W). If None, uses PPI edge.

        Returns:
            Upscaled image with preserved edges (H*scale, W*scale)
        """
        H, W = img.shape
        H2, W2 = H * self.scale_factor, W * self.scale_factor

        # Step 1: Compute guide from MSFA channels (for weight calculation)
        if channels is not None:
            guide = self._compute_msfa_guide(channels)
        else:
            guide = self._compute_edge_guide(img)

        # Step 2: Directional upscale using guide weights
        img_up = self._directional_upscale(img, guide)

        return img_up

    def _directional_upscale(self, img: np.ndarray, guide: np.ndarray, eps: float = 1e-6) -> np.ndarray:
        """Directional interpolation upscale (BTES-style, Eq. 18-21).

        Args:
            img: Image to upscale (H, W)
            guide: Guide image for weight calculation (H, W)
            eps: Small value to prevent division by zero

        Returns:
            Upscaled image (2H, 2W)
        """
        H, W = img.shape
        H2, W2 = H * 2, W * 2

        # Initialize: place original at even positions
        img_2x = np.zeros((H2, W2), dtype=np.float32)
        img_2x[0::2, 0::2] = img

        # Step 1: Diagonal interpolation (odd, odd)
        for i in range(H - 1):
            for j in range(W - 1):
                y, x = 2*i + 1, 2*j + 1

                # 4 diagonal neighbors
                v_nw, v_ne = img[i, j], img[i, j + 1]
                v_se, v_sw = img[i + 1, j + 1], img[i + 1, j]

                # Guide values for weights
                g_nw, g_ne = guide[i, j], guide[i, j + 1]
                g_se, g_sw = guide[i + 1, j + 1], guide[i + 1, j]

                # Weights: inverse of opposite difference (Eq. 19)
                w_nw = 1.0 / (abs(g_nw - g_se) + eps)
                w_ne = 1.0 / (abs(g_ne - g_sw) + eps)
                w_se = 1.0 / (abs(g_se - g_nw) + eps)
                w_sw = 1.0 / (abs(g_sw - g_ne) + eps)

                total = w_nw + w_ne + w_se + w_sw
                img_2x[y, x] = (w_nw*v_nw + w_ne*v_ne + w_se*v_se + w_sw*v_sw) / total

        # Step 2a: Horizontal edge (even, odd)
        for i in range(H):
            for j in range(W - 1):
                y, x = 2*i, 2*j + 1

                v_w, v_e = img_2x[y, 2*j], img_2x[y, 2*j + 2]
                g_w, g_e = guide[i, j], guide[i, j + 1]

                w_w = 1.0 / (abs(g_w - g_e) + eps)
                w_e = 1.0 / (abs(g_e - g_w) + eps)

                weighted_sum = w_w*v_w + w_e*v_e
                total_w = w_w + w_e

                # Add vertical neighbors from Step 1 if available
                if i > 0:
                    v_n = img_2x[y - 1, x]
                    g_n = img_2x[2*(i-1) + 1, x] if i > 0 else g_w
                    g_s_ref = img_2x[y + 1, x] if i < H - 1 else g_n
                    w_n = 1.0 / (abs(g_n - g_s_ref) + eps)
                    weighted_sum += w_n * v_n
                    total_w += w_n

                if i < H - 1:
                    v_s = img_2x[y + 1, x]
                    g_s = img_2x[2*i + 1, x]
                    g_n_ref = img_2x[y - 1, x] if i > 0 else g_s
                    w_s = 1.0 / (abs(g_s - g_n_ref) + eps)
                    weighted_sum += w_s * v_s
                    total_w += w_s

                img_2x[y, x] = weighted_sum / total_w

        # Step 2b: Vertical edge (odd, even)
        for i in range(H - 1):
            for j in range(W):
                y, x = 2*i + 1, 2*j

                v_n, v_s = img_2x[2*i, x], img_2x[2*i + 2, x]
                g_n, g_s = guide[i, j], guide[i + 1, j]

                w_n = 1.0 / (abs(g_n - g_s) + eps)
                w_s = 1.0 / (abs(g_s - g_n) + eps)

                weighted_sum = w_n*v_n + w_s*v_s
                total_w = w_n + w_s

                # Add horizontal neighbors from Step 1 if available
                if j > 0:
                    v_w = img_2x[y, x - 1]
                    g_w = img_2x[y, 2*(j-1) + 1] if j > 0 else g_n
                    g_e_ref = img_2x[y, x + 1] if j < W - 1 else g_w
                    w_w = 1.0 / (abs(g_w - g_e_ref) + eps)
                    weighted_sum += w_w * v_w
                    total_w += w_w

                if j < W - 1:
                    v_e = img_2x[y, x + 1]
                    g_e = img_2x[y, 2*j + 1]
                    g_w_ref = img_2x[y, x - 1] if j > 0 else g_e
                    w_e = 1.0 / (abs(g_e - g_w_ref) + eps)
                    weighted_sum += w_e * v_e
                    total_w += w_e

                img_2x[y, x] = weighted_sum / total_w

        # Fill boundary (last row/column)
        img_2x[-1, :] = img_2x[-2, :]
        img_2x[:, -1] = img_2x[:, -2]

        return img_2x

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
