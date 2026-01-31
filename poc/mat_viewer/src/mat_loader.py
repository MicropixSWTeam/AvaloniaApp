"""MAT 파일 로딩 유틸리티"""
import numpy as np
import scipy.io as sio
from pathlib import Path


class MatLoader:
    def __init__(self, path: Path | str):
        self.path = Path(path)
        self.data = None
        self.img = None

    def load(self) -> np.ndarray:
        """MAT 파일 로드, img 키 반환"""
        self.data = sio.loadmat(str(self.path))
        self.img = self.data['img']  # (H, W, C)
        return self.img

    @property
    def n_channels(self) -> int:
        return self.img.shape[2] if self.img is not None else 0

    def get_channel(self, idx: int) -> np.ndarray:
        """특정 채널 반환 (H, W)"""
        return self.img[:, :, idx]

    def get_channel_stats(self, idx: int) -> dict:
        """채널 통계"""
        ch = self.get_channel(idx)
        return {
            'min': ch.min(),
            'max': ch.max(),
            'mean': ch.mean(),
            'std': ch.std()
        }
