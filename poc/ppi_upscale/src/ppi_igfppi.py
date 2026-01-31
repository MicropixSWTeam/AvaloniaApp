"""IGFPPI (Iterative Guided Filtering PPI) generator.

Implements the algorithm from Section 3.1 of the paper:
1. Simple PPI → 2. Gaussian Low-pass → 3. Iterative Guided Filtering (H/V) → 4. Combine
"""

import numpy as np
from scipy.ndimage import convolve, uniform_filter

from .ppi_simple import PPISimple


class PPIIGFPPI(PPISimple):
    """Generate PPI using Iterative Guided Filtering method."""

    def __init__(
        self,
        input_dir,
        epsilon_pixel: float = 1e-4,
        epsilon_global: float = 1e-3,
        max_iterations: int = 50,
        regularization: float = 1e-6,
    ):
        """Initialize IGFPPI generator.

        Args:
            input_dir: Directory containing *nm.png files
            epsilon_pixel: Pixel-wise stopping threshold (Eq.11-12)
            epsilon_global: Global stopping threshold (Eq.13-14)
            max_iterations: Maximum number of iterations
            regularization: Regularization parameter for guided filter (ε in Eq.8)
        """
        super().__init__(input_dir)
        self.epsilon_pixel = epsilon_pixel
        self.epsilon_global = epsilon_global
        self.max_iterations = max_iterations
        self.regularization = regularization

        # Convergence tracking
        self.iterations_h = 0
        self.iterations_v = 0

    @property
    def method_name(self) -> str:
        return "igfppi"

    def generate_ppi(self) -> np.ndarray:
        """Generate PPI using IGFPPI method.

        Returns:
            np.ndarray: PPI image, shape (H, W), float32 [0, 255]
        """
        # Step 1: Get simple average PPI
        ppi_simple = super().generate_ppi()

        # Step 2: Gaussian low-pass filtering (Eq.4)
        ppi_lowpass = self._gaussian_lowpass(ppi_simple)

        # Step 3: Iterative guided filtering in horizontal and vertical directions
        ppi_h, D_h = self._iterative_guided_filter(
            ppi_simple, ppi_lowpass, direction="horizontal"
        )
        ppi_v, D_v = self._iterative_guided_filter(
            ppi_simple, ppi_lowpass, direction="vertical"
        )

        # Step 4: Combine horizontal and vertical results (Eq.15-16)
        self.ppi = self._combine_hv(ppi_h, ppi_v, D_h, D_v)
        return self.ppi

    def _gaussian_lowpass(self, img: np.ndarray) -> np.ndarray:
        """Apply 5x5 Gaussian low-pass filter (Eq.4b).

        Kernel M = 1/64 * [[1,2,2,2,1],
                           [2,4,4,4,2],
                           [2,4,4,4,2],
                           [2,4,4,4,2],
                           [1,2,2,2,1]]
        """
        kernel = (
            np.array(
                [
                    [1, 2, 2, 2, 1],
                    [2, 4, 4, 4, 2],
                    [2, 4, 4, 4, 2],
                    [2, 4, 4, 4, 2],
                    [1, 2, 2, 2, 1],
                ],
                dtype=np.float32,
            )
            / 64.0
        )
        return convolve(img, kernel, mode="reflect")

    def _iterative_guided_filter(
        self, guide: np.ndarray, input_img: np.ndarray, direction: str
    ) -> tuple[np.ndarray, np.ndarray]:
        """Apply iterative guided filtering until convergence (Eq.9-14).

        Args:
            guide: Guide image (ppi_simple)
            input_img: Input image to filter (ppi_lowpass initially)
            direction: 'horizontal' (h=7, v=3) or 'vertical' (h=3, v=7)

        Returns:
            tuple: (filtered_image, final_D_values)
        """
        if direction == "horizontal":
            window_h, window_v = 7, 3
        else:  # vertical
            window_h, window_v = 3, 7

        current = input_img.copy()
        prev = current.copy()
        D = np.ones_like(current) * np.inf  # Initialize D to large values

        for iteration in range(self.max_iterations):
            # Apply one iteration of guided filter (Eq.6-8)
            filtered = self._guided_filter_step(guide, current, window_h, window_v)

            # Compute pixel-wise difference (Eq.11-12)
            delta = np.abs(filtered - current)
            d = np.abs(filtered - prev)
            D = d * delta

            # Update for next iteration
            prev = current.copy()
            current = filtered

            # Check global stopping criterion (Eq.13-14)
            delta_mad = np.mean(np.abs(filtered - prev))
            if delta_mad < self.epsilon_global:
                break

            # Check pixel-wise stopping criterion
            converged_ratio = np.mean(D < self.epsilon_pixel)
            if converged_ratio > 0.99:  # 99% of pixels converged
                break

        # Track iterations for statistics
        if direction == "horizontal":
            self.iterations_h = iteration + 1
        else:
            self.iterations_v = iteration + 1

        # Ensure D has no zeros (for safe division in combine step)
        D = np.maximum(D, 1e-10)

        return current, D

    def _guided_filter_step(
        self,
        guide: np.ndarray,
        input_img: np.ndarray,
        window_h: int,
        window_v: int,
    ) -> np.ndarray:
        """Apply one step of guided filter (Eq.6-8).

        Linear model: q_i = a_k * I_i + b_k

        Coefficients (Eq.8):
            a_k = (cov(I,p)) / (var(I) + ε)
            b_k = mean(p) - a_k * mean(I)
        """
        # Window size tuple (height, width) for uniform_filter
        size = (window_v, window_h)

        # Compute local means
        mean_I = uniform_filter(guide, size=size, mode="reflect")
        mean_p = uniform_filter(input_img, size=size, mode="reflect")
        mean_Ip = uniform_filter(guide * input_img, size=size, mode="reflect")
        mean_II = uniform_filter(guide * guide, size=size, mode="reflect")

        # Compute covariance and variance
        cov_Ip = mean_Ip - mean_I * mean_p
        var_I = mean_II - mean_I * mean_I

        # Linear coefficients (Eq.8)
        a = cov_Ip / (var_I + self.regularization)
        b = mean_p - a * mean_I

        # Compute mean of a and b over local windows
        mean_a = uniform_filter(a, size=size, mode="reflect")
        mean_b = uniform_filter(b, size=size, mode="reflect")

        # Output: q = mean_a * I + mean_b (Eq.6)
        return mean_a * guide + mean_b

    def _combine_hv(
        self,
        ppi_h: np.ndarray,
        ppi_v: np.ndarray,
        D_h: np.ndarray,
        D_v: np.ndarray,
    ) -> np.ndarray:
        """Combine horizontal and vertical results (Eq.15-16).

        w_h = 1 / D_h,  w_v = 1 / D_v
        result = (w_h * I_h + w_v * I_v) / (w_h + w_v)
        """
        w_h = 1.0 / D_h
        w_v = 1.0 / D_v

        result = (w_h * ppi_h + w_v * ppi_v) / (w_h + w_v)
        return result

    def get_statistics(self) -> dict:
        """Get PPI statistics including iteration counts."""
        stats = super().get_statistics()
        stats["iterations_horizontal"] = self.iterations_h
        stats["iterations_vertical"] = self.iterations_v
        return stats
