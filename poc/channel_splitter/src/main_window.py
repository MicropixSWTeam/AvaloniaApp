"""Main window with drag and drop support for channel splitting."""

from PyQt6.QtWidgets import (
    QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton
)
from PyQt6.QtCore import Qt
from PyQt6.QtGui import QDragEnterEvent, QDropEvent

import os

from .image_splitter import split_image, get_channel_label
from .image_registration import register_channels, export_registered_channels
from .channel_view import ChannelView

# Output directory for exported images
EXPORT_DIR = os.path.join(os.path.dirname(__file__), "../../..", "ppi_upscale/data")


class MainWindow(QMainWindow):
    """Main application window with drag and drop image loading."""

    def __init__(self):
        super().__init__()
        self.channels = []
        self.registered_channels = []
        self.shifts = []
        self.is_registered = False
        self.current_index = 0
        self._setup_ui()
        self.setAcceptDrops(True)

    def _setup_ui(self):
        """Set up the main window UI."""
        self.setWindowTitle("Channel Splitter - 3x5 Tile Viewer")

        central_widget = QWidget()
        self.setCentralWidget(central_widget)

        main_layout = QVBoxLayout(central_widget)
        main_layout.setContentsMargins(10, 10, 10, 10)

        # Channel display area
        self.display_area = QWidget()
        self.display_layout = QVBoxLayout(self.display_area)
        self.display_layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        main_layout.addWidget(self.display_area, 1)

        # Navigation buttons
        nav_layout = QHBoxLayout()
        nav_layout.setSpacing(20)

        self.prev_btn = QPushButton("< Prev")
        self.prev_btn.setFixedSize(100, 40)
        self.prev_btn.clicked.connect(self._prev_channel)
        self.prev_btn.setEnabled(False)
        nav_layout.addWidget(self.prev_btn)

        self.channel_label = QLabel("0 / 0")
        self.channel_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.channel_label.setStyleSheet("font-size: 16px; font-weight: bold;")
        self.channel_label.setFixedWidth(100)
        nav_layout.addWidget(self.channel_label)

        self.next_btn = QPushButton("Next >")
        self.next_btn.setFixedSize(100, 40)
        self.next_btn.clicked.connect(self._next_channel)
        self.next_btn.setEnabled(False)
        nav_layout.addWidget(self.next_btn)

        # Register button
        self.register_btn = QPushButton("Register")
        self.register_btn.setFixedSize(100, 40)
        self.register_btn.clicked.connect(self._toggle_registration)
        self.register_btn.setEnabled(False)
        nav_layout.addWidget(self.register_btn)

        # Export button
        self.export_btn = QPushButton("Export")
        self.export_btn.setFixedSize(100, 40)
        self.export_btn.clicked.connect(self._export_channels)
        self.export_btn.setEnabled(False)
        nav_layout.addWidget(self.export_btn)

        nav_container = QWidget()
        nav_container.setLayout(nav_layout)
        main_layout.addWidget(nav_container, 0, Qt.AlignmentFlag.AlignCenter)

        # Initial placeholder
        self._show_placeholder()

    def _show_placeholder(self):
        """Show placeholder text when no image is loaded."""
        self._clear_display()

        placeholder = QLabel(
            "Drag and drop a 3x5 tiled image here\n\n"
            "Use Prev/Next buttons to navigate channels"
        )
        placeholder.setAlignment(Qt.AlignmentFlag.AlignCenter)
        placeholder.setStyleSheet("""
            QLabel {
                font-size: 16px;
                color: #666;
                padding: 50px;
                border: 2px dashed #ccc;
                border-radius: 10px;
            }
        """)
        self.display_layout.addWidget(placeholder)

    def _clear_display(self):
        """Remove all widgets from the display area."""
        while self.display_layout.count():
            item = self.display_layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()

    def _load_image(self, file_path: str):
        """Load and split an image file."""
        try:
            self.channels, tile_size = split_image(file_path)
            self.registered_channels = []
            self.shifts = []
            self.is_registered = False
            self.register_btn.setText("Register")
            self.register_btn.setEnabled(True)
            self.export_btn.setText("Export")
            self.export_btn.setEnabled(False)
            self.current_index = 0
            self._show_current_channel()
            self._update_nav_buttons()

            # Resize window to fit tile
            self.resize(tile_size[1] + 40, tile_size[0] + 150)
            self.setWindowTitle(
                f"Channel Splitter - {file_path.split('/')[-1]} "
                f"({tile_size[1]}x{tile_size[0]})"
            )
        except Exception as e:
            self._show_error(str(e))

    def _show_current_channel(self):
        """Display the current channel."""
        self._clear_display()

        if not self.channels:
            return

        # Choose registered or original channels
        if self.is_registered and self.registered_channels:
            channel_data = self.registered_channels[self.current_index]
        else:
            channel_data = self.channels[self.current_index]

        # Build label with shift info
        label = get_channel_label(self.current_index)
        if self.is_registered and self.shifts:
            dx, dy = self.shifts[self.current_index]
            label += f"  |  dx: {dx:+.1f}, dy: {dy:+.1f}"

        channel_view = ChannelView(channel_data, label)
        self.display_layout.addWidget(channel_view)

        self.channel_label.setText(f"{self.current_index + 1} / {len(self.channels)}")

    def _update_nav_buttons(self):
        """Update navigation button states."""
        has_channels = len(self.channels) > 0
        self.prev_btn.setEnabled(has_channels and self.current_index > 0)
        self.next_btn.setEnabled(has_channels and self.current_index < len(self.channels) - 1)

    def _toggle_registration(self):
        """Apply registration to all channels."""
        if not self.channels:
            return

        # Compute registration if not done yet
        if not self.registered_channels:
            self.registered_channels, self.shifts = register_channels(self.channels)

        self.is_registered = True
        self.register_btn.setEnabled(False)
        self.register_btn.setText("Registered")
        self.export_btn.setEnabled(True)
        self._show_current_channel()

    def _export_channels(self):
        """Export registered channels to PNG files."""
        if not self.channels or not self.shifts:
            return

        output_dir = os.path.normpath(EXPORT_DIR)
        export_registered_channels(self.channels, self.shifts, output_dir)
        print(f"Files exported to: {output_dir}")

    def _prev_channel(self):
        """Show previous channel."""
        if self.current_index > 0:
            self.current_index -= 1
            self._show_current_channel()
            self._update_nav_buttons()

    def _next_channel(self):
        """Show next channel."""
        if self.current_index < len(self.channels) - 1:
            self.current_index += 1
            self._show_current_channel()
            self._update_nav_buttons()

    def _show_error(self, message: str):
        """Show an error message."""
        self._clear_display()
        self.channels = []
        self.registered_channels = []
        self.shifts = []
        self.is_registered = False
        self.register_btn.setText("Register")
        self.register_btn.setEnabled(False)
        self.export_btn.setText("Export")
        self.export_btn.setEnabled(False)
        self._update_nav_buttons()

        error_label = QLabel(f"Error: {message}\n\nPlease try another image.")
        error_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        error_label.setStyleSheet("font-size: 14px; color: #c00; padding: 30px;")
        self.display_layout.addWidget(error_label)

    def dragEnterEvent(self, event: QDragEnterEvent):
        """Handle drag enter event."""
        if event.mimeData().hasUrls():
            urls = event.mimeData().urls()
            if urls and urls[0].isLocalFile():
                file_path = urls[0].toLocalFile().lower()
                if file_path.endswith(('.png', '.jpg', '.jpeg', '.bmp', '.tiff', '.tif')):
                    event.acceptProposedAction()
                    return
        event.ignore()

    def dropEvent(self, event: QDropEvent):
        """Handle drop event."""
        urls = event.mimeData().urls()
        if urls and urls[0].isLocalFile():
            file_path = urls[0].toLocalFile()
            self._load_image(file_path)
            event.acceptProposedAction()
