"""Base class for PPI generators."""

from abc import ABC, abstractmethod
from pathlib import Path
from typing import Optional

import numpy as np
from PIL import Image


class PPIGeneratorBase(ABC):
    """Base class for Pseudo-Panchromatic Image generators."""

    def __init__(self, input_dir: Path):
        """Initialize with input directory containing channel images.

        Args:
            input_dir: Directory containing *nm.png files (410nm-690nm)
        """
        self.input_dir = Path(input_dir)
        self.channels: Optional[np.ndarray] = None
        self.ppi: Optional[np.ndarray] = None

    @property
    @abstractmethod
    def method_name(self) -> str:
        """Return the method name for identification."""
        pass

    def load_channels(self) -> np.ndarray:
        """Load all multispectral channel images.

        Returns:
            np.ndarray: Shape (N, H, W) with float32 values [0, 255]
        """
        channel_files = sorted(self.input_dir.glob("*nm.png"))

        if not channel_files:
            raise FileNotFoundError(f"No *nm.png files found in {self.input_dir}")

        channels = []
        for filepath in channel_files:
            img = Image.open(filepath).convert("L")  # Convert to grayscale
            arr = np.array(img, dtype=np.float32)
            channels.append(arr)

        self.channels = np.stack(channels, axis=0)
        return self.channels

    @abstractmethod
    def generate_ppi(self) -> np.ndarray:
        """Generate PPI image. Must be implemented by subclasses."""
        pass

    def save_ppi(self, output_path: Path) -> Path:
        """Save PPI as PNG image.

        Args:
            output_path: Output file path

        Returns:
            Path: Saved file path
        """
        if self.ppi is None:
            self.generate_ppi()

        ppi_uint8 = np.clip(self.ppi, 0, 255).astype(np.uint8)
        img = Image.fromarray(ppi_uint8, mode="L")

        output_path = Path(output_path)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        img.save(output_path)

        return output_path

    def get_statistics(self) -> dict:
        """Get PPI statistics.

        Returns:
            dict: Statistics including method, mean, std, min, max
        """
        if self.ppi is None:
            self.generate_ppi()

        return {
            "method": self.method_name,
            "mean": float(np.mean(self.ppi)),
            "std": float(np.std(self.ppi)),
            "min": float(np.min(self.ppi)),
            "max": float(np.max(self.ppi)),
            "shape": self.ppi.shape,
            "num_channels": len(self.channels) if self.channels is not None else 0,
        }
