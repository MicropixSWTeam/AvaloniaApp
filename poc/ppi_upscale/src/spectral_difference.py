"""Spectral difference 계산 (Δ^c = channel - PPI)"""

import numpy as np


def compute_spectral_difference(channel: np.ndarray, ppi: np.ndarray) -> np.ndarray:
    """단일 채널의 spectral difference 계산.

    Args:
        channel: 단일 채널 (H, W)
        ppi: PPI 이미지 (H, W)

    Returns:
        Δ^c = channel - ppi (H, W)
    """
    return channel - ppi


def compute_all_spectral_differences(channels: np.ndarray, ppi: np.ndarray) -> np.ndarray:
    """모든 채널의 spectral difference 계산.

    Args:
        channels: 모든 채널 (N, H, W)
        ppi: PPI 이미지 (H, W)

    Returns:
        Δ (N, H, W)
    """
    return channels - ppi[np.newaxis, :, :]
