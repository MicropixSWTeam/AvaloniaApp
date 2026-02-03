"""Spectral 채널 복원 (Î^c = Î^PPI + Δ^c)"""

import numpy as np


def reconstruct_channel(ppi_2x: np.ndarray, delta_2x: np.ndarray) -> np.ndarray:
    """단일 채널 복원 (Eq. 22).

    Args:
        ppi_2x: 업스케일된 PPI (2H, 2W)
        delta_2x: 업스케일된 spectral difference (2H, 2W)

    Returns:
        복원된 채널 (2H, 2W)
    """
    return ppi_2x + delta_2x


def reconstruct_all_channels(ppi_2x: np.ndarray, deltas_2x: np.ndarray) -> np.ndarray:
    """모든 채널 복원.

    Args:
        ppi_2x: 업스케일된 PPI (2H, 2W)
        deltas_2x: 업스케일된 spectral differences (N, 2H, 2W)

    Returns:
        복원된 채널들 (N, 2H, 2W)
    """
    return ppi_2x[np.newaxis, :, :] + deltas_2x
