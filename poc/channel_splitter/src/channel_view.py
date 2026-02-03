"""Channel view widget for displaying individual channel images."""

import numpy as np
from PyQt6.QtWidgets import QWidget, QVBoxLayout, QLabel
from PyQt6.QtGui import QImage, QPixmap
from PyQt6.QtCore import Qt


class ChannelView(QWidget):
    """Widget to display a single channel image with a label."""

    def __init__(self, channel_data: np.ndarray, label_text: str, parent=None):
        """
        Initialize the channel view.

        Args:
            channel_data: Numpy array of the channel image.
            label_text: Text label for the channel.
            parent: Parent widget.
        """
        super().__init__(parent)
        self._setup_ui(channel_data, label_text)

    def _setup_ui(self, channel_data: np.ndarray, label_text: str):
        """Set up the UI components."""
        layout = QVBoxLayout(self)
        layout.setContentsMargins(5, 5, 5, 5)

        # Channel label
        label = QLabel(label_text)
        label.setStyleSheet("font-weight: bold; font-size: 12px; color: #333;")
        layout.addWidget(label)

        # Image display
        image_label = QLabel()
        pixmap = self._numpy_to_pixmap(channel_data)
        image_label.setPixmap(pixmap)
        image_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(image_label)

        # Add separator line
        separator = QLabel()
        separator.setFixedHeight(1)
        separator.setStyleSheet("background-color: #ccc;")
        layout.addWidget(separator)

    def _numpy_to_pixmap(self, array: np.ndarray) -> QPixmap:
        """Convert numpy array to QPixmap."""
        if array.ndim == 2:
            # Grayscale
            height, width = array.shape
            bytes_per_line = width
            qimg = QImage(
                array.tobytes(),
                width,
                height,
                bytes_per_line,
                QImage.Format.Format_Grayscale8
            )
        elif array.ndim == 3:
            height, width, channels = array.shape
            if channels == 3:
                # RGB
                bytes_per_line = 3 * width
                qimg = QImage(
                    array.tobytes(),
                    width,
                    height,
                    bytes_per_line,
                    QImage.Format.Format_RGB888
                )
            elif channels == 4:
                # RGBA
                bytes_per_line = 4 * width
                qimg = QImage(
                    array.tobytes(),
                    width,
                    height,
                    bytes_per_line,
                    QImage.Format.Format_RGBA8888
                )
            else:
                raise ValueError(f"Unsupported number of channels: {channels}")
        else:
            raise ValueError(f"Unsupported array dimensions: {array.ndim}")

        return QPixmap.fromImage(qimg)
