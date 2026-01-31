"""Spectral 채널 업샘플러 - 전체 파이프라인 wrapper"""

import numpy as np

from .spectral_difference import compute_spectral_difference, compute_all_spectral_differences
from .btes_upsample import btes_upsample
from .spectral_reconstruct import reconstruct_channel, reconstruct_all_channels


class SpectralUpsampler:
    """논문 기반 spectral 채널 2× 업샘플러."""

    def __init__(self, scale_factor: int = 2):
        if scale_factor != 2:
            raise ValueError("Currently only 2× upsampling is supported")
        self.scale_factor = scale_factor

    def upsample_channel(
        self,
        channel: np.ndarray,
        ppi: np.ndarray,
        ppi_2x: np.ndarray
    ) -> np.ndarray:
        """단일 채널 업샘플.

        Args:
            channel: 원본 채널 (H, W)
            ppi: 원본 PPI (H, W)
            ppi_2x: 업스케일된 PPI (2H, 2W)

        Returns:
            업스케일된 채널 (2H, 2W)
        """
        # 1. Spectral difference
        delta = compute_spectral_difference(channel, ppi)

        # 2. BTES upsample
        delta_2x = btes_upsample(delta, ppi_2x)

        # 3. Reconstruct
        return reconstruct_channel(ppi_2x, delta_2x)

    def upsample_all_channels(
        self,
        channels: np.ndarray,
        ppi: np.ndarray,
        ppi_2x: np.ndarray
    ) -> np.ndarray:
        """모든 채널 업샘플.

        Args:
            channels: 원본 채널들 (N, H, W)
            ppi: 원본 PPI (H, W)
            ppi_2x: 업스케일된 PPI (2H, 2W)

        Returns:
            업스케일된 채널들 (N, 2H, 2W)
        """
        n_channels = channels.shape[0]
        h2, w2 = ppi_2x.shape

        # 1. 모든 spectral difference 계산
        deltas = compute_all_spectral_differences(channels, ppi)

        # 2. 각 delta를 BTES로 업샘플
        deltas_2x = np.zeros((n_channels, h2, w2), dtype=np.float32)
        for i in range(n_channels):
            deltas_2x[i] = btes_upsample(deltas[i], ppi_2x)

        # 3. 모든 채널 복원
        return reconstruct_all_channels(ppi_2x, deltas_2x)
