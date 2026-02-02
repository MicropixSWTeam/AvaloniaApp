"""Simple averaging PPI generator.

Implements Equation 3 from the paper:
    I_M = (1/N) × Σ I_c   (c=1 to N, N=15)
"""

import numpy as np

from .base import PPIGeneratorBase


class PPISimple(PPIGeneratorBase):
    """Generate PPI by simple averaging of all channels."""

    @property
    def method_name(self) -> str:
        return "simple"

    def generate_ppi(self) -> np.ndarray:
        """Generate PPI by averaging all channels (Equation 3).

        Returns:
            np.ndarray: PPI image, shape (H, W), float32 [0, 255]
        """
        if self.channels is None:
            self.load_channels()

        # I_M = (1/N) × Σ I_c
        self.ppi = np.mean(self.channels, axis=0)
        return self.ppi
