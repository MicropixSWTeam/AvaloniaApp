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

    def export_channel(self, idx: int, path: Path) -> Path:
        """채널을 grayscale PNG로 저장"""
        ch = self.get_channel(idx)
        # 정규화: min-max → 0-255
        ch_norm = (ch - ch.min()) / (ch.max() - ch.min() + 1e-8)
        ch_uint8 = (ch_norm * 255).astype(np.uint8)

        from PIL import Image
        img = Image.fromarray(ch_uint8, mode='L')
        img.save(path)
        return path

    def export_all_channels(self, output_dir: Path, start_wavelength: int = 400, step: int = 10) -> list[Path]:
        """모든 채널을 {wavelength}nm.png 형식으로 저장"""
        output_dir = Path(output_dir)
        output_dir.mkdir(parents=True, exist_ok=True)

        paths = []
        for i in range(self.n_channels):
            wavelength = start_wavelength + i * step
            path = output_dir / f"{wavelength}nm.png"
            self.export_channel(i, path)
            paths.append(path)
        return paths
