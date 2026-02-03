"""이미지 표시 위젯"""
from PyQt6.QtWidgets import QLabel
from PyQt6.QtGui import QImage, QPixmap
from PyQt6.QtCore import Qt
import numpy as np


class ImageView(QLabel):
    def __init__(self):
        super().__init__()
        self.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.setMinimumSize(400, 400)
        self._arr_uint8 = None  # Keep reference to prevent garbage collection

    def set_image(self, arr: np.ndarray):
        """numpy array를 QPixmap으로 변환하여 표시"""
        # 0~1 → 0~255 정규화
        arr_norm = (arr - arr.min()) / (arr.max() - arr.min() + 1e-8)
        self._arr_uint8 = (arr_norm * 255).astype(np.uint8)

        # Ensure contiguous array for QImage
        self._arr_uint8 = np.ascontiguousarray(self._arr_uint8)

        h, w = self._arr_uint8.shape
        qimg = QImage(self._arr_uint8.data, w, h, w, QImage.Format.Format_Grayscale8)
        pixmap = QPixmap.fromImage(qimg)

        # 위젯 크기에 맞게 스케일
        scaled = pixmap.scaled(
            self.size(),
            Qt.AspectRatioMode.KeepAspectRatio,
            Qt.TransformationMode.SmoothTransformation
        )
        self.setPixmap(scaled)

    def resizeEvent(self, event):
        """창 크기 변경 시 이미지 다시 스케일"""
        super().resizeEvent(event)
        if self._arr_uint8 is not None:
            h, w = self._arr_uint8.shape
            qimg = QImage(self._arr_uint8.data, w, h, w, QImage.Format.Format_Grayscale8)
            pixmap = QPixmap.fromImage(qimg)
            scaled = pixmap.scaled(
                self.size(),
                Qt.AspectRatioMode.KeepAspectRatio,
                Qt.TransformationMode.SmoothTransformation
            )
            self.setPixmap(scaled)
