"""PPID (Mihoubi et al., 2017) PPI generator.

Two-step approach:
1. Low-frequency PPI via Gaussian smoothing
2. High-frequency correction with bilateral-like weighting
"""

import numpy as np
from scipy.ndimage import gaussian_filter

from .ppi_simple import PPISimple


class PPIPPID(PPISimple):
    """Generate PPI using PPID method (Gaussian + high-freq correction)."""

    def __init__(self, input_dir, sigma: float = 1.0, window_size: int = 5):
        """Initialize PPID generator.

        Args:
            input_dir: Directory containing *nm.png files
            sigma: Gaussian filter sigma for low-frequency component
            window_size: Window size for high-frequency correction
        """
        super().__init__(input_dir)
        self.sigma = sigma
        self.window_size = window_size
        self.epsilon = 1e-6

    @property
    def method_name(self) -> str:
        return "ppid"

    def generate_ppi(self) -> np.ndarray:
        """Generate PPI using PPID method.

        Returns:
            np.ndarray: PPI image, shape (H, W), float32 [0, 255]
        """
        # Step 1: Get simple average as base
        ppi_simple = super().generate_ppi()

        # Step 2: Gaussian smoothing for low-frequency component
        ppi_lowfreq = gaussian_filter(ppi_simple, sigma=self.sigma)

        # Step 3: High-frequency correction
        self.ppi = self._apply_highfreq_correction(ppi_simple, ppi_lowfreq)
        return self.ppi

    def _apply_highfreq_correction(
        self,
        ppi_simple: np.ndarray,
        ppi_lowfreq: np.ndarray,
    ) -> np.ndarray:
        """Apply high-frequency correction using local weighted averaging.

        Î^M_k = I^M_k + Σ γ_q (Ī^M_q - I^M_q) / Σ γ_q
        where γ_q = 1 / (|I_k - I_q| + ε)
        """
        h, w = ppi_simple.shape
        pad = self.window_size // 2
        result = np.copy(ppi_simple)

        # Pad images for boundary handling
        simple_pad = np.pad(ppi_simple, pad, mode="reflect")
        lowfreq_pad = np.pad(ppi_lowfreq, pad, mode="reflect")

        # Difference image
        diff_pad = lowfreq_pad - simple_pad

        for i in range(h):
            for j in range(w):
                i_pad, j_pad = i + pad, j + pad
                center_val = simple_pad[i_pad, j_pad]

                # Local neighborhood
                neighborhood = simple_pad[
                    i_pad - pad : i_pad + pad + 1, j_pad - pad : j_pad + pad + 1
                ]
                diff_neighborhood = diff_pad[
                    i_pad - pad : i_pad + pad + 1, j_pad - pad : j_pad + pad + 1
                ]

                # Weights: inverse of intensity difference
                weights = 1.0 / (np.abs(neighborhood - center_val) + self.epsilon)

                # Weighted average of differences
                weighted_diff = np.sum(weights * diff_neighborhood) / np.sum(weights)

                result[i, j] = ppi_simple[i, j] + weighted_diff

        return result
